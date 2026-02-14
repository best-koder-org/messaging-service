using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessagingService.Data;
using MessagingService.DTOs;

namespace MessagingService.Controllers;

/// <summary>
/// Manages read receipt tracking for messages.
/// Uses the Message entity's IsRead/ReadAt fields persisted in the DB.
/// </summary>
[ApiController]
[Route("api/readreceipts")]
[Authorize]
public class ReadReceiptsController : ControllerBase
{
    private readonly ILogger<ReadReceiptsController> _logger;
    private readonly MessagingDbContext _context;

    public ReadReceiptsController(
        ILogger<ReadReceiptsController> logger,
        MessagingDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    /// <summary>
    /// Mark a message as read by the current user.
    /// Updates the Message entity's IsRead and ReadAt fields in the database.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> MarkAsRead([FromBody] MarkAsReadRequest request)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst("preferred_username")?.Value;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == request.MessageId && m.ReceiverId == userId);

        if (message == null)
        {
            _logger.LogWarning("[ReadReceipt] Message {MessageId} not found for user {UserId}",
                request.MessageId, userId);
            return NotFound();
        }

        if (!message.IsRead)
        {
            message.IsRead = true;
            message.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        var receipt = new ReadReceiptDto(
            MessageId: request.MessageId,
            ReaderId: userId,
            ReadAt: message.ReadAt ?? DateTime.UtcNow
        );

        _logger.LogInformation(
            "[ReadReceipt] Message {MessageId} marked as read by {UserId}",
            request.MessageId, userId);

        return Ok(receipt);
    }

    /// <summary>
    /// Get unread message count for a conversation.
    /// Queries the database for messages where IsRead is false.
    /// </summary>
    [HttpGet("{conversationId}/unread-count")]
    public async Task<IActionResult> GetUnreadCount(string conversationId)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst("preferred_username")?.Value;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var unreadCount = await _context.Messages
            .CountAsync(m =>
                m.ConversationId == conversationId
                && m.ReceiverId == userId
                && !m.IsRead
                && !m.IsDeleted);

        var lastRead = await _context.Messages
            .Where(m =>
                m.ConversationId == conversationId
                && m.ReceiverId == userId
                && m.IsRead)
            .OrderByDescending(m => m.ReadAt)
            .Select(m => m.ReadAt)
            .FirstOrDefaultAsync();

        var result = new UnreadCountDto(
            ConversationId: conversationId,
            UnreadCount: unreadCount,
            LastReadAt: lastRead ?? DateTime.MinValue
        );

        return Ok(result);
    }

    /// <summary>
    /// Get read receipt for a specific message.
    /// </summary>
    [HttpGet("message/{messageId:int}")]
    public async Task<IActionResult> GetReceiptsForMessage(int messageId)
    {
        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null || !message.IsRead)
            return Ok(Array.Empty<ReadReceiptDto>());

        var receipt = new ReadReceiptDto(
            MessageId: messageId,
            ReaderId: message.ReceiverId,
            ReadAt: message.ReadAt ?? DateTime.UtcNow
        );

        return Ok(new[] { receipt });
    }
}
