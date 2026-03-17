using DatingApp.Llm;
using DatingApp.Llm;
using MessagingService.Data;
using MessagingService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MessagingService.Controllers;

[ApiController]
[Route("api/messages/moderation")]
[Authorize]
public class ModerationController : ControllerBase
{
    private readonly MessagingDbContext _db;
    private readonly ILogger<ModerationController> _logger;
    private readonly LlmRouter _llmRouter;

    public ModerationController(MessagingDbContext db, ILogger<ModerationController> logger, LlmRouter llmRouter)
    {
        _db = db;
        _logger = logger;
        _llmRouter = llmRouter;
    }

    /// <summary>Get flagged messages that require review</summary>
    [HttpGet("queue")]
    public async Task<IActionResult> GetModerationQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _db.Messages
            .Where(m => m.ModerationStatus == ModerationStatus.RequiresReview || m.IsFlagged)
            .OrderByDescending(m => m.SentAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id,
                m.SenderId,
                m.ReceiverId,
                m.Content,
                m.SentAt,
                m.FlagReason,
                m.ModerationStatus,
                m.ConversationId
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>Review a flagged message (approve or reject)</summary>
    [HttpPost("{id}/review")]
    public async Task<IActionResult> ReviewMessage(int id, [FromBody] ReviewRequest request)
    {
        var message = await _db.Messages.FindAsync(id);
        if (message == null) return NotFound();

        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        message.ModerationStatus = request.Approved ? ModerationStatus.Approved : ModerationStatus.Rejected;
        message.ModeratedAt = DateTime.UtcNow;
        message.ModeratedBy = userId;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Message {Id} reviewed by {Moderator}: {Status}",
            id, userId, message.ModerationStatus);

        return Ok(new { message.Id, message.ModerationStatus });
    }

    /// <summary>Get moderation stats</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var total = await _db.Messages.CountAsync();
        var flagged = await _db.Messages.CountAsync(m => m.IsFlagged);
        var pending = await _db.Messages.CountAsync(m => m.ModerationStatus == ModerationStatus.RequiresReview);
        var rejected = await _db.Messages.CountAsync(m => m.ModerationStatus == ModerationStatus.Rejected);

        var (tokensUsed, tokenBudget, primaryProvider) = _llmRouter.GetUsageStats();
        return Ok(new
        {
            totalMessages = total,
            flaggedMessages = flagged,
            pendingReview = pending,
            rejectedMessages = rejected,
            flagRate = total > 0 ? (double)flagged / total : 0,
            llm = new
            {
                tokensUsedToday = tokensUsed,
                dailyBudget = tokenBudget,
                budgetUsedPercent = tokenBudget > 0 ? (double)tokensUsed / tokenBudget * 100 : 0,
                primaryProvider
            }
        });
    }
}

public class ReviewRequest
{
    public bool Approved { get; set; }
}
