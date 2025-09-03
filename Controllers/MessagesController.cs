using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MessagingService.Services;
using System.Security.Claims;

namespace MessagingService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(IMessageService messageService, ILogger<MessagesController> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        try
        {
            var conversations = await _messageService.GetConversationsAsync(userId);
            return Ok(conversations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting conversations for user {userId}");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("conversation/{otherUserId}")]
    public async Task<IActionResult> GetConversation(string otherUserId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        try
        {
            var messages = await _messageService.GetConversationAsync(userId, otherUserId, page, pageSize);
            return Ok(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting conversation between {userId} and {otherUserId}");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("{messageId}/read")]
    public async Task<IActionResult> MarkAsRead(int messageId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        try
        {
            await _messageService.MarkAsReadAsync(messageId, userId);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error marking message {messageId} as read for user {userId}");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        try
        {
            var success = await _messageService.DeleteMessageAsync(messageId, userId);
            if (success)
            {
                return Ok();
            }
            return NotFound("Message not found or you don't have permission to delete it");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting message {messageId} for user {userId}");
            return StatusCode(500, "Internal server error");
        }
    }
}
