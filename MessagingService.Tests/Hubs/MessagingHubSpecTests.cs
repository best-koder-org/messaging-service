using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MessagingService.DTOs;
using MessagingService.Hubs;
using MessagingService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;
using Microsoft.AspNetCore.SignalR.Client;

namespace MessagingService.Tests.Hubs;

/// <summary>
/// Integration tests for MessagingHub.Spec using SignalR TestServer
/// Tests match-based messaging, acknowledgments, and error handling
/// Uses int matchId/messageId matching MatchmakingService entity types
/// </summary>
public class MessagingHubSpecTests_Fixed : IAsyncLifetime
{
    private IHost _host = null!;
    private HubConnection _user1Connection = null!;
    private HubConnection _user2Connection = null!;
    private const string User1Id = "user-111";
    private const string User2Id = "user-222";
    private const int TestMatchId = 42;

    private Mock<IMessageServiceSpec> _mockMessageService = null!;
    private Mock<IContentModerationService> _mockContentModeration = null!;
    private Mock<ISafetyServiceClient> _mockSafetyService = null!;

    public async Task InitializeAsync()
    {
        _mockMessageService = new Mock<IMessageServiceSpec>();
        _mockContentModeration = new Mock<IContentModerationService>();
        _mockSafetyService = new Mock<ISafetyServiceClient>();

        _mockMessageService
            .Setup(x => x.IsMatchParticipant(TestMatchId, User1Id))
            .ReturnsAsync(true);
        _mockMessageService
            .Setup(x => x.IsMatchParticipant(TestMatchId, User2Id))
            .ReturnsAsync(true);
        
        _mockMessageService
            .Setup(x => x.GetOtherParticipant(TestMatchId, User1Id))
            .ReturnsAsync(User2Id);
        _mockMessageService
            .Setup(x => x.GetOtherParticipant(TestMatchId, User2Id))
            .ReturnsAsync(User1Id);

        _mockMessageService
            .Setup(x => x.SendMessageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((int matchId, string senderId, string body) => new MessageDto
            {
                MessageId = 1,
                MatchId = matchId,
                SenderId = senderId,
                Body = body,
                BodyType = "Text",
                SentAt = DateTime.UtcNow
            });

        _mockContentModeration
            .Setup(x => x.ModerateContentAsync(It.IsAny<string>()))
            .ReturnsAsync(new ModerationResult { IsApproved = true });

        _mockSafetyService
            .Setup(x => x.IsBlockedAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        _host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSignalR();
                        services.AddSingleton(_mockMessageService.Object);
                        services.AddSingleton(_mockContentModeration.Object);
                        services.AddSingleton(_mockSafetyService.Object);
                        services.AddLogging();
                    })
                    .Configure(app =>
                    {
                        app.Use(async (context, next) =>
                        {
                            var userId = context.Request.Query["userId"].ToString();
                            if (!string.IsNullOrEmpty(userId))
                            {
                                var claims = new[]
                                {
                                    new Claim(ClaimTypes.NameIdentifier, userId),
                                    new Claim("sub", userId)
                                };
                                var identity = new ClaimsIdentity(claims, "Test");
                                context.User = new ClaimsPrincipal(identity);
                            }
                            await next();
                        });

                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapHub<MessagingHubSpec>("/hubs/messages");
                        });
                    });
            })
            .StartAsync();

        var server = _host.GetTestServer();

        _user1Connection = new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}hubs/messages?userId={User1Id}", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();

        _user2Connection = new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}hubs/messages?userId={User2Id}", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();

        await _user1Connection.StartAsync();
        await _user2Connection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_user1Connection != null)
        {
            await _user1Connection.StopAsync();
            await _user1Connection.DisposeAsync();
        }
        if (_user2Connection != null)
        {
            await _user2Connection.StopAsync();
            await _user2Connection.DisposeAsync();
        }
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [Fact]
    public async Task SendMessage_ValidMatchBasedMessage_ReceiverGetsNotification()
    {
        var messageReceived = new TaskCompletionSource<MessageDto>();
        _user2Connection!.On<MessageDto>("MessageReceived", msg =>
        {
            messageReceived.SetResult(msg);
        });

        await _user1Connection!.InvokeAsync("SendMessage", new SendMessageRequest
        {
            MatchId = TestMatchId,
            Body = "Hello from user1!"
        });

        var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(5000));
        Assert.True(completed == messageReceived.Task, "Message not received within timeout");
        
        var message = await messageReceived.Task;
        Assert.Equal(User1Id, message.SenderId);
        Assert.Equal("Hello from user1!", message.Body);
        Assert.Equal(TestMatchId, message.MatchId);
    }

    [Fact]
    public async Task SendMessage_ValidMessage_SenderGetsOwnCopy()
    {
        var messageReceived = new TaskCompletionSource<MessageDto>();
        _user1Connection!.On<MessageDto>("MessageReceived", msg =>
        {
            messageReceived.SetResult(msg);
        });

        await _user1Connection.InvokeAsync("SendMessage", new SendMessageRequest
        {
            MatchId = TestMatchId,
            Body = "Echo test"
        });

        var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(5000));
        Assert.True(completed == messageReceived.Task, "Sender didn't receive own message");
        
        var message = await messageReceived.Task;
        Assert.Equal("Echo test", message.Body);
    }

    [Fact]
    public async Task SendMessage_NotMatchParticipant_ThrowsNotAuthorized()
    {
        _mockMessageService!
            .Setup(x => x.IsMatchParticipant(TestMatchId, User1Id))
            .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<HubException>(async () =>
        {
            await _user1Connection!.InvokeAsync("SendMessage", new SendMessageRequest
            {
                MatchId = TestMatchId,
                Body = "Unauthorized message"
            });
        });
        
        Assert.Contains("not-authorized", exception.Message);
    }

    [Fact]
    public async Task SendMessage_BlockedUser_ThrowsMessagingBlocked()
    {
        _mockSafetyService!
            .Setup(x => x.IsBlockedAsync(User1Id, User2Id))
            .ReturnsAsync(true);

        var exception = await Assert.ThrowsAsync<HubException>(async () =>
        {
            await _user1Connection!.InvokeAsync("SendMessage", new SendMessageRequest
            {
                MatchId = TestMatchId,
                Body = "Blocked message"
            });
        });
        
        Assert.Contains("messaging-blocked", exception.Message);
    }

    [Fact]
    public async Task SendMessage_ContentModeration_BlocksInappropriateContent()
    {
        _mockContentModeration!
            .Setup(x => x.ModerateContentAsync("badword"))
            .ReturnsAsync(new ModerationResult 
            { 
                IsApproved = false, 
                Reason = "Inappropriate content" 
            });

        var exception = await Assert.ThrowsAsync<HubException>(async () =>
        {
            await _user1Connection!.InvokeAsync("SendMessage", new SendMessageRequest
            {
                MatchId = TestMatchId,
                Body = "badword"
            });
        });
        
        Assert.Contains("content-blocked", exception.Message);
    }

    [Fact]
    public async Task SendMessage_MessageTooLong_ThrowsError()
    {
        var longMessage = new string('x', 1001);

        var exception = await Assert.ThrowsAsync<HubException>(async () =>
        {
            await _user1Connection!.InvokeAsync("SendMessage", new SendMessageRequest
            {
                MatchId = TestMatchId,
                Body = longMessage
            });
        });
        
        Assert.Contains("message-too-long", exception.Message);
    }

    [Fact]
    public async Task Acknowledge_ValidMessageId_CallsServiceMethod()
    {
        var messageId = 99;

        await _user2Connection!.InvokeAsync("Acknowledge", new AcknowledgeRequest
        {
            MessageId = messageId
        });

        await Task.Delay(100);
        _mockMessageService!.Verify(
            x => x.AcknowledgeMessageAsync(messageId, User2Id),
            Times.Once);
    }

    [Fact]
    public void Connection_BothUsersConnect_Successfully()
    {
        Assert.Equal(HubConnectionState.Connected, _user1Connection!.State);
        Assert.Equal(HubConnectionState.Connected, _user2Connection!.State);
    }

    [Fact]
    public async Task SendMessage_VerifiesMatchOwnershipWithService()
    {
        await _user1Connection!.InvokeAsync("SendMessage", new SendMessageRequest
        {
            MatchId = TestMatchId,
            Body = "Test"
        });

        await Task.Delay(100);
        _mockMessageService!.Verify(
            x => x.IsMatchParticipant(TestMatchId, User1Id),
            Times.Once);
    }

    [Fact]
    public async Task SendMessage_CallsContentModeration()
    {
        const string testMessage = "Clean message";

        await _user1Connection!.InvokeAsync("SendMessage", new SendMessageRequest
        {
            MatchId = TestMatchId,
            Body = testMessage
        });

        await Task.Delay(100);
        _mockContentModeration!.Verify(
            x => x.ModerateContentAsync(testMessage),
            Times.Once);
    }

    [Fact]
    public async Task SendMessage_ChecksBlockStatus()
    {
        await _user1Connection!.InvokeAsync("SendMessage", new SendMessageRequest
        {
            MatchId = TestMatchId,
            Body = "Safety check"
        });

        await Task.Delay(100);
        _mockSafetyService!.Verify(
            x => x.IsBlockedAsync(User1Id, User2Id),
            Times.Once);
        _mockSafetyService.Verify(
            x => x.IsBlockedAsync(User2Id, User1Id),
            Times.Once);
    }
}
