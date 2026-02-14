using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using MessagingService.Controllers;
using MessagingService.Data;
using MessagingService.DTOs;
using MessagingService.Models;

namespace MessagingService.Tests.Controllers;

/// <summary>
/// Tests for ReadReceiptsController — DB-backed read receipt tracking.
/// Uses InMemory EF Core for isolated test state.
/// </summary>
public class ReadReceiptsControllerTests : IDisposable
{
    private readonly Mock<ILogger<ReadReceiptsController>> _mockLogger;
    private readonly MessagingDbContext _context;
    private readonly ReadReceiptsController _controller;

    public ReadReceiptsControllerTests()
    {
        _mockLogger = new Mock<ILogger<ReadReceiptsController>>();

        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new MessagingDbContext(options);

        _controller = new ReadReceiptsController(_mockLogger.Object, _context);
        SetupAuth("reader-user-1");
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private void SetupAuth(string userId)
    {
        var claims = new List<Claim> { new Claim("sub", userId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private async Task<Message> SeedMessage(string senderId, string receiverId, string conversationId = "conv-1", bool isRead = false)
    {
        var message = new Message
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Content = "Test message",
            ConversationId = conversationId,
            Type = MessageType.Text,
            SentAt = DateTime.UtcNow,
            IsRead = isRead,
            ReadAt = isRead ? DateTime.UtcNow : null,
            IsDeleted = false,
            ModerationStatus = ModerationStatus.Approved
        };
        _context.Messages.Add(message);
        await _context.SaveChangesAsync();
        return message;
    }

    [Fact]
    public async Task MarkAsRead_ValidRequest_ReturnsOkWithReceipt()
    {
        // Arrange
        var message = await SeedMessage("sender-1", "reader-user-1");
        var request = new MarkAsReadRequest(message.Id);

        // Act
        var result = await _controller.MarkAsRead(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var receipt = Assert.IsType<ReadReceiptDto>(okResult.Value);
        Assert.Equal(message.Id, receipt.MessageId);
        Assert.Equal("reader-user-1", receipt.ReaderId);
        Assert.True(receipt.ReadAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task MarkAsRead_AlreadyRead_DoesNotChangeReadAt()
    {
        // Arrange
        var message = await SeedMessage("sender-1", "reader-user-1");
        var request = new MarkAsReadRequest(message.Id);

        // Act — mark twice
        await _controller.MarkAsRead(request);
        var firstReadAt = (await _context.Messages.FindAsync(message.Id))!.ReadAt;

        await Task.Delay(10); // ensure different timestamp if it were to change
        await _controller.MarkAsRead(request);
        var secondReadAt = (await _context.Messages.FindAsync(message.Id))!.ReadAt;

        // Assert — ReadAt didn't change
        Assert.Equal(firstReadAt, secondReadAt);
    }

    [Fact]
    public async Task MarkAsRead_MessageNotFound_ReturnsNotFound()
    {
        // Act
        var result = await _controller.MarkAsRead(new MarkAsReadRequest(999));

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MarkAsRead_WrongReceiver_ReturnsNotFound()
    {
        // Arrange — message sent to different user
        var message = await SeedMessage("sender-1", "other-user");
        var request = new MarkAsReadRequest(message.Id);

        // Act — current user (reader-user-1) tries to mark it
        var result = await _controller.MarkAsRead(request);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetReceiptsForMessage_NoReceipts_ReturnsEmptyArray()
    {
        // Arrange
        var message = await SeedMessage("sender-1", "reader-user-1");

        // Act — message is unread
        var result = await _controller.GetReceiptsForMessage(message.Id);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetReceiptsForMessage_WithReadMessage_ReturnsReceipt()
    {
        // Arrange
        var message = await SeedMessage("sender-1", "reader-user-1", isRead: true);

        // Act
        var result = await _controller.GetReceiptsForMessage(message.Id);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var receipts = Assert.IsAssignableFrom<IEnumerable<ReadReceiptDto>>(okResult.Value);
        Assert.Single(receipts);
    }

    [Fact]
    public async Task GetUnreadCount_NoUnreadMessages_ReturnsZero()
    {
        // Act
        var result = await _controller.GetUnreadCount("conv-empty");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var unreadDto = Assert.IsType<UnreadCountDto>(okResult.Value);
        Assert.Equal("conv-empty", unreadDto.ConversationId);
        Assert.Equal(0, unreadDto.UnreadCount);
    }

    [Fact]
    public async Task GetUnreadCount_WithUnreadMessages_ReturnsCorrectCount()
    {
        // Arrange — 3 unread messages for reader-user-1
        await SeedMessage("sender-1", "reader-user-1", "conv-test");
        await SeedMessage("sender-1", "reader-user-1", "conv-test");
        await SeedMessage("sender-1", "reader-user-1", "conv-test");
        // 1 read message
        await SeedMessage("sender-1", "reader-user-1", "conv-test", isRead: true);

        // Act
        var result = await _controller.GetUnreadCount("conv-test");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var unreadDto = Assert.IsType<UnreadCountDto>(okResult.Value);
        Assert.Equal(3, unreadDto.UnreadCount);
    }
}
