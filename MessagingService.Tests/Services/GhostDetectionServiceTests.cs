using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MessagingService.Data;
using MessagingService.Models;
using MessagingService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace MessagingService.Tests.Services;

public class GhostDetectionServiceTests : IDisposable
{
    private readonly MessagingDbContext _context;
    private readonly IConfiguration _config;

    public GhostDetectionServiceTests()
    {
        var opts = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseInMemoryDatabase($"Ghost_{Guid.NewGuid()}").Options;
        _context = new MessagingDbContext(opts);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["GhostDetection:CheckIntervalHours"] = "6",
                ["GhostDetection:InactivityThresholdDays"] = "3",
                ["GhostDetection:MinMutualMessages"] = "3",
                ["Gateway:BaseUrl"] = "http://localhost:8080",
                ["InternalAuth:ApiKey"] = "test-key",
            })
            .Build();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private GhostDetectionService CreateService(Mock<HttpMessageHandler>? httpMock = null)
    {
        httpMock ??= CreateSuccessMock();
        var httpClient = new HttpClient(httpMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8080")
        };
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient("ReputationService"))
            .Returns(httpClient);

        // Build a real service provider from ServiceCollection
        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton<IHttpClientFactory>(httpClientFactory.Object);
        var serviceProvider = services.BuildServiceProvider();

        return new GhostDetectionService(
            serviceProvider,
            NullLogger<GhostDetectionService>.Instance,
            _config);
    }

    private static Mock<HttpMessageHandler> CreateSuccessMock()
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
        return mock;
    }

    private void SeedConversation(string convId, string userA, string userB,
        int msgsFromA, int msgsFromB, DateTime lastMsgAt)
    {
        var now = lastMsgAt;
        for (int i = 0; i < msgsFromA; i++)
        {
            _context.Messages.Add(new Message
            {
                SenderId = userA, ReceiverId = userB,
                ConversationId = convId, Content = $"A msg {i}",
                SentAt = now, ModerationStatus = ModerationStatus.Approved,
            });
            now = now.AddMinutes(1);
        }
        for (int i = 0; i < msgsFromB; i++)
        {
            _context.Messages.Add(new Message
            {
                SenderId = userB, ReceiverId = userA,
                ConversationId = convId, Content = $"B msg {i}",
                SentAt = now, ModerationStatus = ModerationStatus.Approved,
            });
            now = now.AddMinutes(1);
        }
        _context.SaveChanges();
    }

    [Fact]
    public async Task DetectsGhost_WhenBothSentEnoughAndNoReply()
    {
        // Both sent 3 messages, last was 4 days ago from B, A never replied
        SeedConversation("conv1", "alice", "bob", 3, 3,
            DateTime.UtcNow.AddDays(-4));
        // B sent the last message (Seeded as alice→bob→bob→alice... wait)
        // Actually with my seeding: A sends 3, then B sends 3.
        // Last sender = B (bob), last receiver = A (alice) — alice ghosted
        // Need to ensure the LAST message is from the person who DIDN'T ghost
        // Wait — ghost is "receiver of last message". So alice received last
        // message from bob and didn't reply. Alice is the ghost.

        var service = CreateService();
        var tracking = _context.Set<GhostTracking>();

        // Act
        await service.DetectAndReportGhostsAsync(CancellationToken.None);

        // Assert
        var tracked = await tracking.ToListAsync();
        Assert.Single(tracked);
        Assert.Equal("alice", tracked[0].GhostUserId);
        Assert.True(tracked[0].Reported);
    }

    [Fact]
    public async Task SkipsRecentConversations()
    {
        // Last message was 1 hour ago — too recent
        SeedConversation("conv2", "alice", "bob", 3, 3,
            DateTime.UtcNow.AddHours(-1));

        var service = CreateService();
        var tracking = _context.Set<GhostTracking>();

        await service.DetectAndReportGhostsAsync(CancellationToken.None);

        Assert.Empty(await tracking.ToListAsync());
    }

    [Fact]
    public async Task SkipsInsufficientMutualMessages()
    {
        // Alice sent 5, Bob sent only 1 — not mutual enough
        SeedConversation("conv3", "alice", "bob", 5, 1,
            DateTime.UtcNow.AddDays(-5));

        var service = CreateService();
        var tracking = _context.Set<GhostTracking>();

        await service.DetectAndReportGhostsAsync(CancellationToken.None);

        Assert.Empty(await tracking.ToListAsync());
    }

    [Fact]
    public async Task SkipsAlreadyTrackedConversations()
    {
        SeedConversation("conv4", "alice", "bob", 3, 3,
            DateTime.UtcNow.AddDays(-4));

        // Record as already processed
        _context.Set<GhostTracking>().Add(new GhostTracking
        {
            GhostUserId = "alice", VictimUserId = "bob",
            ConversationId = "conv4", Reported = true,
        });
        _context.SaveChanges();

        var service = CreateService();
        var tracking = _context.Set<GhostTracking>();

        await service.DetectAndReportGhostsAsync(CancellationToken.None);

        // Should still have only 1 entry (no duplicate)
        Assert.Single(await tracking.ToListAsync());
    }

    [Fact]
    public async Task HandlesHttpFailure_Gracefully()
    {
        SeedConversation("conv5", "alice", "bob", 3, 3,
            DateTime.UtcNow.AddDays(-4));

        // HTTP call returns 500
        var httpMock = new Mock<HttpMessageHandler>();
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateService(httpMock);
        var tracking = _context.Set<GhostTracking>();

        await service.DetectAndReportGhostsAsync(CancellationToken.None);

        // Should still record the tracking entry (with Reported=false)
        var tracked = await tracking.ToListAsync();
        Assert.Single(tracked);
        Assert.False(tracked[0].Reported);
    }
}
