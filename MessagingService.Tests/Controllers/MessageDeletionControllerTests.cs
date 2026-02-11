using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MessagingService.Controllers;
using MessagingService.Data;
using MessagingService.Models;

namespace MessagingService.Tests.Controllers;

public class MessageDeletionControllerTests : IDisposable
{
    private readonly MessagingDbContext _context;
    private readonly Mock<ILogger<MessageDeletionController>> _mockLogger;
    private readonly MessageDeletionController _controller;

    public MessageDeletionControllerTests()
    {
        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseInMemoryDatabase($"MessageDeletion_{Guid.NewGuid()}")
            .Options;
        _context = new MessagingDbContext(options);
        _mockLogger = new Mock<ILogger<MessageDeletionController>>();
        _controller = new MessageDeletionController(_context, _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task DeleteUserMessages_WithMessages_ReturnsCount()
    {
        // Arrange
        _context.Messages.AddRange(
            new Message { SenderId = "user-1", ReceiverId = "user-2", Content = "Hi", ConversationId = "c1", SentAt = DateTime.UtcNow },
            new Message { SenderId = "user-2", ReceiverId = "user-1", Content = "Hey", ConversationId = "c1", SentAt = DateTime.UtcNow },
            new Message { SenderId = "user-3", ReceiverId = "user-4", Content = "Unrelated", ConversationId = "c2", SentAt = DateTime.UtcNow }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteUserMessages("user-1");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("2", okResult.Value);
        Assert.Equal(1, await _context.Messages.CountAsync());
    }

    [Fact]
    public async Task DeleteUserMessages_NoMessages_ReturnsZero()
    {
        var result = await _controller.DeleteUserMessages("nonexistent");
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("0", okResult.Value);
    }

    [Fact]
    public async Task DeleteUserMessages_DeletesSentAndReceived()
    {
        // Arrange — user is both sender and receiver in different conversations
        _context.Messages.AddRange(
            new Message { SenderId = "target", ReceiverId = "other1", Content = "Sent", ConversationId = "c1", SentAt = DateTime.UtcNow },
            new Message { SenderId = "other2", ReceiverId = "target", Content = "Received", ConversationId = "c2", SentAt = DateTime.UtcNow }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteUserMessages("target");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("2", okResult.Value);
        Assert.Equal(0, await _context.Messages.CountAsync());
    }
}
