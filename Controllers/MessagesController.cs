using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MessagingService.Services;
using MessagingService.Commands;
using MessagingService.Queries;
using MessagingService.Common;
using MessagingService.Data;
using MessagingService.DTOs;
using MediatR;
using System.Security.Claims;

namespace MessagingService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly ILogger<MessagesController> _logger;
    private readonly IMediator _mediator;
    private readonly MessagingDbContext _context;

    public MessagesController(ILogger<MessagesController> logger, IMediator mediator, MessagingDbContext context)
    {
        _logger = logger;
        _mediator = mediator;
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequestRest request)
    {
        _logger.LogInformation("POST /api/messages called. User.Identity.IsAuthenticated: {IsAuth}, Claims count: {ClaimCount}",
            User.Identity?.IsAuthenticated, User.Claims.Count());

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _logger.LogInformation("Extracted userId from ClaimTypes.NameIdentifier: {UserId}", userId ?? "NULL");

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Unauthorized: No userId found in claims. Available claims: {Claims}",
                string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));
            return Unauthorized();
        }

        var command = new SendMessageCommand
        {
            SenderId = userId,
            ReceiverId = request.RecipientUserId,
            Content = request.Text,
            Type = request.Type ?? Models.MessageType.Text
        };

        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            // Return 403 Forbidden for unauthorized access (non-matched users)
            if (result.Error?.Contains("UNAUTHORIZED") == true || result.Error?.Contains("non-matched") == true)
            {
                return Forbid();
            }
            return StatusCode(500, ApiResponse<object>.FailureResult(result.Error!));
        }

        return Created($"/api/messages/{result.Value!.Id}", ApiResponse<object>.SuccessResult(result.Value));
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var query = new GetConversationsQuery { UserId = userId };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return StatusCode(500, ApiResponse<object>.FailureResult(result.Error!));
        }

        return Ok(ApiResponse<object>.SuccessResult(result.Value!));
    }

    [HttpGet("conversation/{otherUserId}")]
    public async Task<IActionResult> GetConversation(
        string otherUserId,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] int? limit = null,
        [FromQuery] int? offset = null)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // Support both page/pageSize and limit/offset parameter styles
        var actualPageSize = limit ?? pageSize ?? 50;
        var actualPage = page ?? (offset.HasValue ? (offset.Value / actualPageSize) + 1 : 1);

        var query = new GetConversationQuery
        {
            UserId = userId,
            OtherUserId = otherUserId,
            Page = actualPage,
            PageSize = actualPageSize
        };
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return StatusCode(500, ApiResponse<object>.FailureResult(result.Error!));
        }

        return Ok(ApiResponse<object>.SuccessResult(result.Value!));
    }

    [HttpPost("{messageId}/read")]
    public async Task<IActionResult> MarkAsRead(int messageId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var command = new MarkMessageAsReadCommand { MessageId = messageId, UserId = userId };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return StatusCode(500, ApiResponse<object>.FailureResult(result.Error!));
        }

        return Ok(ApiResponse<object>.SuccessResult(new { }));
    }

    [HttpDelete("{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var command = new DeleteMessageCommand { MessageId = messageId, UserId = userId };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            if (result.Error!.Contains("not found"))
            {
                return NotFound(ApiResponse<object>.FailureResult(result.Error!));
            }
            return StatusCode(500, ApiResponse<object>.FailureResult(result.Error!));
        }

        return Ok(ApiResponse<object>.SuccessResult(new { }));
    }

    /// <summary>
    /// Delete all messages for a specific user (used during account deletion)
    /// </summary>
    [HttpDelete("user/{userId}")]
    [AllowAnonymous]
    public async Task<IActionResult> DeleteUserMessages(string userId)
    {
        try
        {
            _logger.LogInformation("Deleting all messages for user {UserId}", userId);

            var messages = await _context.Messages
                .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                .ToListAsync();

            var count = messages.Count;
            _context.Messages.RemoveRange(messages);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted {Count} messages for user {UserId}", count, userId);
            return Ok(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting messages for user {UserId}", userId);
            return StatusCode(500, "An error occurred while deleting user messages");
        }
    }
}
