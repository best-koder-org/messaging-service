using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MessagingService.Data;
using MessagingService.Models;
using MessagingService.Services;

namespace MessagingService.Tests.Services;

public class MessageServiceTests : IDisposable
{
    private readonly MessagingDbContext _context;
    private readonly Mock<IMatchValidationService> _matchValidationMock;
    private readonly MessageService _service;

    public MessageServiceTests()
    {
        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseInMemoryDatabase($"MessageService_{Guid.NewGuid()}")
            .Options;
        _context = new MessagingDbContext(options);

        _matchValidationMock = new Mock<IMatchValidationService>();
        // Default: users are matched
        _matchValidationMock
            .Setup(m => m.AreUsersMatchedAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        _service = new MessageService(
            _context,
            Mock.Of<ILogger<MessageService>>(),
            _matchValidationMock.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ===== SendMessageAsync =====

    [Fact]
    public async Task SendMessage_MatchedUsers_CreatesMessage()
    {
        var msg = await _service.SendMessageAsync("alice", "bob", "Hello!");

        Assert.NotNull(msg);
        Assert.Equal("alice", msg.SenderId);
        Assert.Equal("bob", msg.ReceiverId);
        Assert.Equal("Hello!", msg.Content);
        Assert.False(msg.IsRead);
    }

    [Fact]
    public async Task SendMessage_UnmatchedUsers_ThrowsUnauthorized()
    {
        _matchValidationMock
            .Setup(m => m.AreUsersMatchedAsync("stranger1", "stranger2"))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.SendMessageAsync("stranger1", "stranger2", "Hey"));
    }

    [Fact]
    public async Task SendMessage_SetsConversationId()
    {
        var msg = await _service.SendMessageAsync("alice", "bob", "Test");

        Assert.NotEmpty(msg.ConversationId);
    }

    [Fact]
    public async Task SendMessage_ConversationId_IsDeterministic()
    {
        var msg1 = await _service.SendMessageAsync("alice", "bob", "First");
        var msg2 = await _service.SendMessageAsync("bob", "alice", "Reply");

        // ConversationId should be the same regardless of direction
        Assert.Equal(msg1.ConversationId, msg2.ConversationId);
    }

    [Fact]
    public async Task SendMessage_ConversationId_SortsAlphabetically()
    {
        var msg = await _service.SendMessageAsync("zack", "alice", "Hello");

        // GenerateConversationId sorts: "alice" < "zack" → "alice_zack"
        Assert.Equal("alice_zack", msg.ConversationId);
    }

    [Fact]
    public async Task SendMessage_PersistsToDatabase()
    {
        await _service.SendMessageAsync("alice", "bob", "Persisted");

        var stored = await _context.Messages.FirstOrDefaultAsync(m => m.Content == "Persisted");
        Assert.NotNull(stored);
        Assert.Equal("alice", stored.SenderId);
    }

    // ===== GetMessagesAsync =====

    [Fact]
    public async Task GetMessages_ReturnsBetweenTwoUsers()
    {
        await _service.SendMessageAsync("alice", "bob", "Hi");
        await _service.SendMessageAsync("bob", "alice", "Hey");
        await _service.SendMessageAsync("alice", "charlie", "Other chat");

        var messages = await _service.GetConversationAsync("alice", "bob");

        Assert.Equal(2, messages.Count());
    }

    // ===== DeleteMessageAsync =====

    [Fact]
    public async Task DeleteMessage_BySender_ReturnsTrue()
    {
        var msg = await _service.SendMessageAsync("alice", "bob", "Delete me");

        var result = await _service.DeleteMessageAsync(msg.Id, "alice");

        Assert.True(result);
    }

    [Fact]
    public async Task DeleteMessage_ByNonSender_ReturnsFalse()
    {
        var msg = await _service.SendMessageAsync("alice", "bob", "Not yours");

        var result = await _service.DeleteMessageAsync(msg.Id, "bob");

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteMessage_NonExistent_ReturnsFalse()
    {
        var result = await _service.DeleteMessageAsync(999, "alice");

        Assert.False(result);
    }

    // ===== MarkAsReadAsync =====

    [Fact]
    public async Task MarkAsRead_ByReceiver_SetsIsRead()
    {
        var msg = await _service.SendMessageAsync("alice", "bob", "Read me");

        await _service.MarkAsReadAsync(msg.Id, "bob");

        var updated = await _context.Messages.FindAsync(msg.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsRead);
        Assert.NotNull(updated.ReadAt);
    }

    [Fact]
    public async Task MarkAsRead_BySender_DoesNotMarkRead()
    {
        var msg = await _service.SendMessageAsync("alice", "bob", "Should not read");

        await _service.MarkAsReadAsync(msg.Id, "alice");

        var updated = await _context.Messages.FindAsync(msg.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsRead);
    }

    [Fact]
    public async Task MarkAsRead_AlreadyRead_DoesNotChangeTimestamp()
    {
        var msg = await _service.SendMessageAsync("alice", "bob", "Already read");
        await _service.MarkAsReadAsync(msg.Id, "bob");

        var firstRead = (await _context.Messages.FindAsync(msg.Id))!.ReadAt;

        await Task.Delay(10); // Ensure time difference
        await _service.MarkAsReadAsync(msg.Id, "bob");

        var secondRead = (await _context.Messages.FindAsync(msg.Id))!.ReadAt;
        Assert.Equal(firstRead, secondRead);
    }
}
