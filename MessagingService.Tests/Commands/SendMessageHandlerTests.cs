using System.Threading;
using System.Threading.Tasks;
using MessagingService.Commands;
using MessagingService.Hubs;
using MessagingService.Models;
using MessagingService.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

#nullable enable

namespace MessagingService.Tests.HandlerTests;

public class SendMessageHandlerTests
{
    private static Message NewMessage(string senderId, string receiverId) => new()
    {
        Id = 42,
        SenderId = senderId,
        ReceiverId = receiverId,
        Content = "hej",
        SentAt = System.DateTime.UtcNow,
        IsRead = false,
        Type = MessageType.Text,
        ConversationId = "conv"
    };

    [Fact]
    public async Task Handle_OnSuccess_BroadcastsMessageReceivedToReceiverAndSender()
    {
        var msgService = new Mock<IMessageService>();
        msgService
            .Setup(s => s.SendMessageAsync("sender-kc", "receiver-kc", "hej", MessageType.Text, It.IsAny<bool>()))
            .ReturnsAsync(NewMessage("sender-kc", "receiver-kc"));

        var receiverProxy = new Mock<IClientProxy>();
        var senderProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.User("receiver-kc")).Returns(receiverProxy.Object);
        clients.Setup(c => c.User("sender-kc")).Returns(senderProxy.Object);
        var hubContext = new Mock<IHubContext<MessagingHubSpec>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var handler = new SendMessageHandler(
            msgService.Object,
            Mock.Of<ILogger<SendMessageHandler>>(),
            hubContext.Object);

        var result = await handler.Handle(
            new SendMessageCommand
            {
                SenderId = "sender-kc",
                ReceiverId = "receiver-kc",
                Content = "hej"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        receiverProxy.Verify(p => p.SendCoreAsync(
            "MessageReceived",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
        senderProxy.Verify(p => p.SendCoreAsync(
            "MessageReceived",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_HubBroadcastFails_StillReturnsSuccess()
    {
        var msgService = new Mock<IMessageService>();
        msgService
            .Setup(s => s.SendMessageAsync("a", "b", "x", MessageType.Text, It.IsAny<bool>()))
            .ReturnsAsync(NewMessage("a", "b"));

        var failingProxy = new Mock<IClientProxy>();
        failingProxy
            .Setup(p => p.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Exception("hub down"));
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(failingProxy.Object);
        var hubContext = new Mock<IHubContext<MessagingHubSpec>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var handler = new SendMessageHandler(
            msgService.Object,
            Mock.Of<ILogger<SendMessageHandler>>(),
            hubContext.Object);

        var result = await handler.Handle(
            new SendMessageCommand { SenderId = "a", ReceiverId = "b", Content = "x" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NullHubContext_DoesNotThrow()
    {
        var msgService = new Mock<IMessageService>();
        msgService
            .Setup(s => s.SendMessageAsync("a", "b", "x", MessageType.Text, It.IsAny<bool>()))
            .ReturnsAsync(NewMessage("a", "b"));

        var handler = new SendMessageHandler(
            msgService.Object,
            Mock.Of<ILogger<SendMessageHandler>>(),
            hubContext: null);

        var result = await handler.Handle(
            new SendMessageCommand { SenderId = "a", ReceiverId = "b", Content = "x" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
