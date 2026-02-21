using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessagingService.Data;
using MessagingService.DTOs;

namespace MessagingService.Controllers;

/// <summary>
/// Manages read receipt tracking for messages.
/// DB-backed via MessagingDbContext — updates Message.IsRead/ReadAt fields.
/// </summary>
[ApiController]
[Route("api/readreceipts")]
[Authorize]
public class ReadReceiptsController : ControllerBase
{
    private readonly ILogger<ReadReceiptsController> _logger;
    private readonly MessagingDbContext _context;

    public ReadReceiptsController(ILogger<ReadReceiptsController> logger, MessagingDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    /// <summary>
    /// Mark a message as read by the current user.
    /// Only the receiver can mark their own messages as read.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> MarkAsRead([FromBody] MarkAsReadRequest request)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst("preferred_username")?.Value
            ?? "unknown";

        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == request.MessageId && m.ReceiverId == userId);

        if (message == null)
            return NotFound();

        if (!message.IsRead)
        {
            message.IsRead = true;
            message.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        var receipt = new ReadReceiptDto(
            MessageId: message.Id,
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
    /// </summary>
    [HttpGet("{conversationId}/unread-count")]
    public async Task<IActionResult> GetUnreadCount(string conversationId)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst("preferred_username")?.Value
            ?? "unknown";

        var unreadCount = await _context.Messages
            .CountAsync(m => m.ConversationId == conversationId
                          && m.ReceiverId == userId
                          && !m.IsRead
                          && !m.IsDeleted);

        var lastRead = await _context.Messages
            .Where(m => m.ConversationId == conversationId
                     && m.ReceiverId == userId
                     && m.IsRead)
            .OrderByDescending(m => m.ReadAt)
            .Select(m => m.ReadAt)
            .FirstOrDefaultAsync();

        return Ok(new UnreadCountDto(
            ConversationId: conversationId,
            UnreadCount: unreadCount,
            LastReadAt: lastRead
        ));
    }

    /// <summary>
    /// Get all read receipts for a specific message.
    /// </summary>
    [HttpGet("message/{messageId:int}")]
    public async Task<IActionResult> GetReceiptsForMessage(int messageId)
    {
        var message = await _context.Messages.FindAsync(messageId);

        if (message == null || !message.IsRead)
            return Ok(Array.Empty<ReadReceiptDto>());

        var receipts = new[]
        {
            new ReadReceiptDto(
                MessageId: message.Id,
                ReaderId: message.ReceiverId,
                ReadAt: message.ReadAt ?? DateTime.UtcNow
            )
        };

        return Ok(receipts);
    }
}
