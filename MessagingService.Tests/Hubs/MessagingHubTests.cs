using System.Threading.Tasks;
using System;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using MessagingService.Data;
using MessagingService.Hubs;
using MessagingService.Models;
using MessagingService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace MessagingService.Tests.Hubs;

public class MessagingHubTests : IAsyncLifetime
{
    private IHost? _host;
    private HubConnection? _senderConnection;
    private HubConnection? _receiverConnection;
    private const string SenderId = "user-123";
    private const string ReceiverId = "user-456";

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddDbContext<MessagingDbContext>(options =>
                            options.UseInMemoryDatabase($"MessagingHubTests_{Guid.NewGuid()}"));

                        // Add minimal authentication for testing
                        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                            .AddJwtBearer(options =>
                            {
                                options.TokenValidationParameters = new TokenValidationParameters
                                {
                                    ValidateIssuer = false,
                                    ValidateAudience = false,
                                    ValidateLifetime = true,
                                    ValidateIssuerSigningKey = true,
                                    IssuerSigningKey = new SymmetricSecurityKey(
                                        System.Text.Encoding.UTF8.GetBytes("TestSecretKey123456789012345678901234567890"))
                                };
                                options.Events = new JwtBearerEvents
                                {
                                    OnMessageReceived = context =>
                                    {
                                        var accessToken = context.Request.Query["access_token"];
                                        if (!string.IsNullOrEmpty(accessToken))
                                        {
                                            context.Token = accessToken;
                                        }
                                        return Task.CompletedTask;
                                    }
                                };
                            });
                        
                        services.AddAuthorization();

                        services.AddSignalR();
                        services.AddScoped<IMessageService, MessageService>();

                        // Mock content moderation service
                        var mockContentModeration = new Mock<IContentModerationService>();
                        mockContentModeration
                            .Setup(x => x.ModerateContentAsync(It.IsAny<string>()))
                            .ReturnsAsync(new ModerationResult { IsApproved = true });
                        services.AddSingleton(mockContentModeration.Object);

                        var mockSpamDetection = new Mock<ISpamDetectionService>();
                        mockSpamDetection
                            .Setup(x => x.IsSpamAsync(It.IsAny<string>(), It.IsAny<string>()))
                            .ReturnsAsync(false);
                        services.AddSingleton(mockSpamDetection.Object);

                        var mockRateLimiting = new Mock<IRateLimitingService>();
                        mockRateLimiting
                            .Setup(x => x.IsAllowedAsync(It.IsAny<string>()))
                            .ReturnsAsync(true);
                        services.AddSingleton(mockRateLimiting.Object);

                        var mockReporting = new Mock<IReportingService>();
                        mockReporting
                            .Setup(x => x.IsUserBannedAsync(It.IsAny<string>()))
                            .ReturnsAsync(false);
                        services.AddSingleton(mockReporting.Object);

                        services.AddLogging();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapHub<MessagingHub>("/messagingHub");
                        });
                    });
            })
            .StartAsync();

        var server = _host.GetTestServer();
        
        _senderConnection = new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}messagingHub", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(GenerateTestToken(SenderId));
            })
            .Build();

        _receiverConnection = new HubConnectionBuilder()
            .WithUrl($"{server.BaseAddress}messagingHub", options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(GenerateTestToken(ReceiverId));
            })
            .Build();

        await _senderConnection.StartAsync();
        await _receiverConnection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_senderConnection != null)
        {
            await _senderConnection.StopAsync();
            await _senderConnection.DisposeAsync();
        }

        if (_receiverConnection != null)
        {
            await _receiverConnection.StopAsync();
            await _receiverConnection.DisposeAsync();
        }

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [Fact]
    public async Task SendMessage_ValidMessage_ReceiverGetsNotification()
    {
        var messageReceived = new TaskCompletionSource<bool>();
        string? receivedContent = null;

        _receiverConnection!.On<object>("ReceiveMessage", message =>
        {
            var msg = System.Text.Json.JsonSerializer.Serialize(message);
            var doc = System.Text.Json.JsonDocument.Parse(msg);
            receivedContent = doc.RootElement.GetProperty("Content").GetString();
            messageReceived.SetResult(true);
        });

        await _senderConnection!.InvokeAsync("SendMessage", ReceiverId, "Hello!", 0);

        var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(5000));
        Assert.True(completed == messageReceived.Task, "Message not received within timeout");
        Assert.Equal("Hello!", receivedContent);
    }

    [Fact]
    public async Task SendMessage_ValidMessage_SenderGetsConfirmation()
    {
        var messageSent = new TaskCompletionSource<bool>();
        int? messageId = null;

        _senderConnection!.On<object>("MessageSent", message =>
        {
            var msg = System.Text.Json.JsonSerializer.Serialize(message);
            var doc = System.Text.Json.JsonDocument.Parse(msg);
            messageId = doc.RootElement.GetProperty("Id").GetInt32();
            messageSent.SetResult(true);
        });

        await _senderConnection.InvokeAsync("SendMessage", ReceiverId, "Test message", 0);

        var completed = await Task.WhenAny(messageSent.Task, Task.Delay(5000));
        Assert.True(completed == messageSent.Task, "Confirmation not received");
        Assert.NotNull(messageId);
        Assert.True(messageId > 0);
    }

    [Fact]
    public async Task SendMessage_PersistsToDatabase()
    {
        var messageSent = new TaskCompletionSource<bool>();
        _senderConnection!.On<object>("MessageSent", _ => messageSent.SetResult(true));

        await _senderConnection.InvokeAsync("SendMessage", ReceiverId, "Persistence test", 0);
        await messageSent.Task;

        using var scope = _host!.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        var message = await context.Messages
            .FirstOrDefaultAsync(m => m.Content == "Persistence test");

        Assert.NotNull(message);
        Assert.Equal(SenderId, message.SenderId);
        Assert.Equal(ReceiverId, message.ReceiverId);
    }

    [Fact]
    public async Task Connection_BothUsersConnect_Successfully()
    {
        Assert.Equal(HubConnectionState.Connected, _senderConnection!.State);
        Assert.Equal(HubConnectionState.Connected, _receiverConnection!.State);
    }

    private string GenerateTestToken(string userId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId),
            new Claim(ClaimTypes.Name, $"TestUser_{userId}"),
        };

        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("TestSecretKey123456789012345678901234567890"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "test",
            audience: "test",
            claims: claims,
            expires: DateTime.Now.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
