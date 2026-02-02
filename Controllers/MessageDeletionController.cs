using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessagingService.Data;

namespace MessagingService.Controllers
{
    [Route("api/messages")]
    [ApiController]
    public class MessageDeletionController : ControllerBase
    {
        private readonly MessagingDbContext _context;
        private readonly ILogger<MessageDeletionController> _logger;

        public MessageDeletionController(MessagingDbContext context, ILogger<MessageDeletionController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Cascade delete all messages for a user (for account deletion).
        /// </summary>
        /// <param name="userId">The Keycloak userId (Guid as string)</param>
        /// <returns>Count of messages deleted as plain text</returns>
        [HttpDelete("user/{userId}")]
        [AllowAnonymous] // Service-to-service call from UserService
        public async Task<IActionResult> DeleteUserMessages(string userId)
        {
            try
            {
                var messages = await _context.Messages
                    .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                    .ToListAsync();

                var count = messages.Count;

                if (count > 0)
                {
                    _context.Messages.RemoveRange(messages);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Deleted {Count} messages for user {UserId}", count, userId);
                }

                return Ok(count.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting messages for user {UserId}", userId);
                return StatusCode(500, "0");
            }
        }
    }
}
