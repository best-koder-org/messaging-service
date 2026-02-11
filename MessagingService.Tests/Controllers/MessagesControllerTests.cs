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
using MediatR;
using MessagingService.Controllers;
using MessagingService.Commands;
using CmdMessageDto = MessagingService.Commands.MessageDto;
using MessagingService.Queries;
using MessagingService.Common;
using MessagingService.DTOs;
using MessagingService.Data;

using MessagingService.Models;

namespace MessagingService.Tests.Controllers;

/// <summary>
/// Tests for MessagesController — REST API for sending, reading, and deleting messages.
/// Uses MediatR for most operations, direct DbContext for user message deletion.
/// </summary>
public class MessagesControllerTests : IDisposable
{
    private readonly Mock<ILogger<MessagesController>> _mockLogger;
    private readonly Mock<IMediator> _mockMediator;
    private readonly MessagingDbContext _context;
    private readonly MessagesController _controller;
    private const string TestUserId = "user-abc-123";

    public MessagesControllerTests()
    {
        _mockLogger = new Mock<ILogger<MessagesController>>();
        _mockMediator = new Mock<IMediator>();

        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseInMemoryDatabase($"MessagesController_{Guid.NewGuid()}")
            .Options;
        _context = new MessagingDbContext(options);

        _controller = new MessagesController(_mockLogger.Object, _mockMediator.Object, _context);
        SetupAuth(TestUserId);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private void SetupAuth(string userId)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private void SetupNoAuth()
    {
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
    }

    // --- SendMessage ---

    [Fact]
    public async Task SendMessage_Success_ReturnsCreated()
    {
        // Arrange
        var request = new SendMessageRequestRest { RecipientUserId = "user-def-456", Text = "Hello!" };
        var msgDto = new Commands.MessageDto { Id = 1, SenderId = TestUserId, ReceiverId = "user-def-456", Content = "Hello!", SentAt = DateTime.UtcNow };
        _mockMediator.Setup(m => m.Send(It.IsAny<SendMessageCommand>(), default))
            .ReturnsAsync(Result<Commands.MessageDto>.Success(msgDto));

        // Act
        var result = await _controller.SendMessage(request);

        // Assert
        var createdResult = Assert.IsType<CreatedResult>(result);
        Assert.Contains("/api/messages/1", createdResult.Location!);
    }

    [Fact]
    public async Task SendMessage_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        SetupNoAuth();
        var request = new SendMessageRequestRest { RecipientUserId = "user-def-456", Text = "Hello!" };

        // Act
        var result = await _controller.SendMessage(request);

        // Assert
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task SendMessage_NonMatchedUsers_ReturnsForbid()
    {
        // Arrange
        var request = new SendMessageRequestRest { RecipientUserId = "stranger", Text = "Hi!" };
        _mockMediator.Setup(m => m.Send(It.IsAny<SendMessageCommand>(), default))
            .ReturnsAsync(Result<Commands.MessageDto>.Failure("UNAUTHORIZED: non-matched users"));

        // Act
        var result = await _controller.SendMessage(request);

        // Assert
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task SendMessage_ServerError_Returns500()
    {
        // Arrange
        var request = new SendMessageRequestRest { RecipientUserId = "user-def-456", Text = "Hello!" };
        _mockMediator.Setup(m => m.Send(It.IsAny<SendMessageCommand>(), default))
            .ReturnsAsync(Result<Commands.MessageDto>.Failure("Database connection lost"));

        // Act
        var result = await _controller.SendMessage(request);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    // --- GetConversations ---

    [Fact]
    public async Task GetConversations_Success_ReturnsOk()
    {
        // Arrange
        var conversations = new List<object> { new { Id = "conv1" }, new { Id = "conv2" } };
        _mockMediator.Setup(m => m.Send(It.IsAny<GetConversationsQuery>(), default))
            .ReturnsAsync(Result<object>.Success(conversations));

        // Act
        var result = await _controller.GetConversations();

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetConversations_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        SetupNoAuth();

        // Act
        var result = await _controller.GetConversations();

        // Assert
        Assert.IsType<UnauthorizedResult>(result);
    }

    // --- GetConversation ---

    [Fact]
    public async Task GetConversation_Success_ReturnsOk()
    {
        // Arrange
        var messages = new List<object> { new { Id = 1, Content = "Hi" } };
        _mockMediator.Setup(m => m.Send(It.IsAny<GetConversationQuery>(), default))
            .ReturnsAsync(Result<object>.Success(messages));

        // Act
        var result = await _controller.GetConversation("other-user-id");

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetConversation_NoAuth_ReturnsUnauthorized()
    {
        SetupNoAuth();
        var result = await _controller.GetConversation("other");
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetConversation_WithPagination_PassesParametersToQuery()
    {
        // Arrange
        _mockMediator.Setup(m => m.Send(It.Is<GetConversationQuery>(q =>
            q.Page == 2 && q.PageSize == 10), default))
            .ReturnsAsync(Result<object>.Success(new List<object>()));

        // Act
        var result = await _controller.GetConversation("other", page: 2, pageSize: 10);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        _mockMediator.Verify(m => m.Send(It.Is<GetConversationQuery>(q =>
            q.Page == 2 && q.PageSize == 10), default), Times.Once);
    }

    // --- MarkAsRead ---

    [Fact]
    public async Task MarkAsRead_Success_ReturnsOk()
    {
        // Arrange
        _mockMediator.Setup(m => m.Send(It.IsAny<MarkMessageAsReadCommand>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _controller.MarkAsRead(42);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task MarkAsRead_NoAuth_ReturnsUnauthorized()
    {
        SetupNoAuth();
        var result = await _controller.MarkAsRead(42);
        Assert.IsType<UnauthorizedResult>(result);
    }

    // --- DeleteMessage ---

    [Fact]
    public async Task DeleteMessage_Success_ReturnsOk()
    {
        // Arrange
        _mockMediator.Setup(m => m.Send(It.IsAny<DeleteMessageCommand>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _controller.DeleteMessage(1);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task DeleteMessage_NotFound_ReturnsNotFound()
    {
        // Arrange
        _mockMediator.Setup(m => m.Send(It.IsAny<DeleteMessageCommand>(), default))
            .ReturnsAsync(Result.Failure("Message not found"));

        // Act
        var result = await _controller.DeleteMessage(999);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteMessage_NoAuth_ReturnsUnauthorized()
    {
        SetupNoAuth();
        var result = await _controller.DeleteMessage(1);
        Assert.IsType<UnauthorizedResult>(result);
    }

    // --- DeleteUserMessages ---

    [Fact]
    public async Task DeleteUserMessages_WithMessages_ReturnsCount()
    {
        // Arrange
        _context.Messages.AddRange(
            new Message { SenderId = "victim", ReceiverId = "other1", Content = "Hi", ConversationId = "conv1", SentAt = DateTime.UtcNow },
            new Message { SenderId = "other2", ReceiverId = "victim", Content = "Hey", ConversationId = "conv2", SentAt = DateTime.UtcNow },
            new Message { SenderId = "unrelated1", ReceiverId = "unrelated2", Content = "Nope", ConversationId = "conv3", SentAt = DateTime.UtcNow }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteUserMessages("victim");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, okResult.Value);
        Assert.Equal(1, await _context.Messages.CountAsync()); // Only unrelated message remains
    }

    [Fact]
    public async Task DeleteUserMessages_NoMessages_ReturnsZero()
    {
        // Act
        var result = await _controller.DeleteUserMessages("nobody");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0, okResult.Value);
    }
}
