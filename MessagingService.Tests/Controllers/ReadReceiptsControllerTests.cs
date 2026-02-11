using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using MessagingService.Controllers;
using MessagingService.DTOs;

namespace MessagingService.Tests.Controllers;

/// <summary>
/// Tests for ReadReceiptsController — in-memory read receipt tracking.
/// NOTE: Static dictionary means tests share state. Use unique messageIds per test.
/// </summary>
public class ReadReceiptsControllerTests
{
    private readonly Mock<ILogger<ReadReceiptsController>> _mockLogger;
    private readonly ReadReceiptsController _controller;

    public ReadReceiptsControllerTests()
    {
        _mockLogger = new Mock<ILogger<ReadReceiptsController>>();
        _controller = new ReadReceiptsController(_mockLogger.Object);
        SetupAuth("reader-user-1");
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

    [Fact]
    public void MarkAsRead_ValidRequest_ReturnsOkWithReceipt()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var request = new MarkAsReadRequest(messageId);

        // Act
        var result = _controller.MarkAsRead(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var receipt = Assert.IsType<ReadReceiptDto>(okResult.Value);
        Assert.Equal(messageId, receipt.MessageId);
        Assert.Equal("reader-user-1", receipt.ReaderId);
        Assert.True(receipt.ReadAt <= DateTime.UtcNow);
    }

    [Fact]
    public void MarkAsRead_DuplicateForSameReader_DoesNotAddDuplicate()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var request = new MarkAsReadRequest(messageId);

        // Act — mark twice
        _controller.MarkAsRead(request);
        _controller.MarkAsRead(request);

        // Assert — get receipts and verify only one
        var getResult = _controller.GetReceiptsForMessage(messageId);
        var okResult = Assert.IsType<OkObjectResult>(getResult);
        var receipts = Assert.IsAssignableFrom<IEnumerable<ReadReceiptDto>>(okResult.Value);
        Assert.Single(receipts);
    }

    [Fact]
    public void GetReceiptsForMessage_NoReceipts_ReturnsEmptyArray()
    {
        // Act
        var result = _controller.GetReceiptsForMessage(Guid.NewGuid());

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void GetReceiptsForMessage_WithReceipts_ReturnsAll()
    {
        // Arrange — two different readers mark same message
        var messageId = Guid.NewGuid();
        var request = new MarkAsReadRequest(messageId);

        SetupAuth("reader-A");
        _controller.MarkAsRead(request);

        SetupAuth("reader-B");
        _controller.MarkAsRead(request);

        // Act
        var result = _controller.GetReceiptsForMessage(messageId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var receipts = Assert.IsAssignableFrom<IEnumerable<ReadReceiptDto>>(okResult.Value);
        Assert.Equal(2, receipts.Count());
    }

    [Fact]
    public void GetUnreadCount_ReturnsZeroForMvp()
    {
        // Act — MVP always returns 0 unread
        var result = _controller.GetUnreadCount("conv-123");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var unreadDto = Assert.IsType<UnreadCountDto>(okResult.Value);
        Assert.Equal("conv-123", unreadDto.ConversationId);
        Assert.Equal(0, unreadDto.UnreadCount);
    }
}
