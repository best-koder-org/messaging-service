using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MessagingService.Services;
using MessagingService.Commands;
using MessagingService.Queries;
using MessagingService.Common;
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

    public MessagesController(ILogger<MessagesController> logger, IMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
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
    public async Task<IActionResult> GetConversation(string otherUserId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var query = new GetConversationQuery 
        { 
            UserId = userId, 
            OtherUserId = otherUserId, 
            Page = page, 
            PageSize = pageSize 
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
}
