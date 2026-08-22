using MessagingService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MessagingService.Controllers;

/// <summary>
/// Dev/staging-only administrative reset endpoints.
/// Wipes all messages so a clean MVP demo can begin.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly MessagingDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AdminController> _logger;

    public AdminController(MessagingDbContext context, IWebHostEnvironment env, ILogger<AdminController> logger)
    {
        _context = context;
        _env = env;
        _logger = logger;
    }

    private bool IsResetAllowed() =>
        _env.IsDevelopment() || _env.IsStaging() || _env.EnvironmentName == "Demo";

    /// <summary>Wipe all messages. Dev/Staging/Demo only.</summary>
    [HttpDelete("messages")]
    public async Task<IActionResult> ResetAllMessages()
    {
        if (!IsResetAllowed())
        {
            _logger.LogWarning("Admin reset rejected: environment={Env} is not dev/staging/demo", _env.EnvironmentName);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Admin reset disabled in this environment." });
        }

        var messages = await _context.Messages.ToListAsync();
        var count = messages.Count;
        _context.Messages.RemoveRange(messages);
        await _context.SaveChangesAsync();

        _logger.LogWarning(
            "[FINDING] High AdminReset: cleared {Count} messages by {User}",
            count, User.Identity?.Name ?? "unknown");

        return Ok(new
        {
            message = "Messages cleared.",
            deletedMessages = count,
            environment = _env.EnvironmentName,
        });
    }

    /// <summary>
    /// Targeted purge: deletes ONLY bot-generated messages (IsBotGenerated=true).
    /// Real-user messages are never touched. Dev/Staging/Demo only.
    /// </summary>
    [HttpDelete("bot-messages")]
    public async Task<IActionResult> ResetBotMessages([FromQuery] int olderThanHours = 0)
    {
        if (!IsResetAllowed())
        {
            _logger.LogWarning("Admin reset rejected: environment={Env} is not dev/staging/demo", _env.EnvironmentName);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Admin reset disabled in this environment." });
        }

        // Optional TTL filter: only purge bot messages older than N hours.
        var cutoff = olderThanHours > 0 ? DateTime.UtcNow.AddHours(-olderThanHours) : (DateTime?)null;
        var botMessages = await _context.Messages
            .Where(m => m.IsBotGenerated && (cutoff == null || m.SentAt < cutoff)).ToListAsync();
        var count = botMessages.Count;
        _context.Messages.RemoveRange(botMessages);
        await _context.SaveChangesAsync();

        _logger.LogWarning(
            "[FINDING] Medium AdminReset: cleared {Count} bot messages by {User}",
            count, User.Identity?.Name ?? "unknown");

        return Ok(new
        {
            message = "Bot-generated messages cleared.",
            deletedMessages = count,
            environment = _env.EnvironmentName,
        });
    }
}
