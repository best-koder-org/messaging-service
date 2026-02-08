using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MessagingService.DTOs;

namespace MessagingService.Controllers;

/// <summary>
/// Manages read receipt tracking for messages.
/// Phase 1: REST API only. Phase 2 will add SignalR real-time push.
/// </summary>
[ApiController]
[Route("api/readreceipts")]
[Authorize]
public class ReadReceiptsController : ControllerBase
{
    private readonly ILogger<ReadReceiptsController> _logger;

    // In-memory store for MVP — will be replaced with EF/Redis
    private static readonly Dictionary<Guid, List<ReadReceiptDto>> _receipts = new();
    private static readonly object _lock = new();

    public ReadReceiptsController(ILogger<ReadReceiptsController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Mark a message as read by the current user.
    /// </summary>
    [HttpPost]
    public IActionResult MarkAsRead([FromBody] MarkAsReadRequest request)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst("preferred_username")?.Value
            ?? "unknown";

        var receipt = new ReadReceiptDto(
            MessageId: request.MessageId,
            ReaderId: userId,
            ReadAt: DateTime.UtcNow
        );

        lock (_lock)
        {
            if (!_receipts.ContainsKey(request.MessageId))
                _receipts[request.MessageId] = new List<ReadReceiptDto>();

            // Don't add duplicate receipts for same reader
            if (!_receipts[request.MessageId].Any(r => r.ReaderId == userId))
            {
                _receipts[request.MessageId].Add(receipt);
            }
        }

        _logger.LogInformation(
            "[ReadReceipt] Message {MessageId} marked as read by {UserId}",
            request.MessageId, userId);

        return Ok(receipt);
    }

    /// <summary>
    /// Get unread message count for a conversation.
    /// </summary>
    [HttpGet("{conversationId}/unread-count")]
    public IActionResult GetUnreadCount(string conversationId)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst("preferred_username")?.Value
            ?? "unknown";

        // MVP: return 0 unread since we don't have conversation-message mapping yet
        // This endpoint structure is correct for when we wire up the real DB
        var result = new UnreadCountDto(
            ConversationId: conversationId,
            UnreadCount: 0,
            LastReadAt: DateTime.UtcNow
        );

        return Ok(result);
    }

    /// <summary>
    /// Get all read receipts for a specific message.
    /// </summary>
    [HttpGet("message/{messageId:guid}")]
    public IActionResult GetReceiptsForMessage(Guid messageId)
    {
        lock (_lock)
        {
            if (_receipts.TryGetValue(messageId, out var receipts))
                return Ok(receipts);
        }

        return Ok(Array.Empty<ReadReceiptDto>());
    }
}
