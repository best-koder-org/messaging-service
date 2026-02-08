using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MessagingService.DTOs;
using System.Collections.Concurrent;

namespace MessagingService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TypingController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, List<TypingIndicatorDto>> _typingState = new();

    [HttpPost]
    public IActionResult PostTypingState([FromBody] TypingIndicatorDto dto)
    {
        var key = dto.ConversationId;
        _typingState.AddOrUpdate(key,
            _ => new List<TypingIndicatorDto> { dto },
            (_, list) =>
            {
                list.RemoveAll(t => t.UserId == dto.UserId);
                if (dto.IsTyping) list.Add(dto);
                return list;
            });
        return Ok();
    }

    [HttpGet("{conversationId}")]
    public IActionResult GetTypingUsers(string conversationId)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-10);
        if (_typingState.TryGetValue(conversationId, out var list))
        {
            var active = list.Where(t => t.Timestamp > cutoff).ToList();
            return Ok(active);
        }
        return Ok(Array.Empty<TypingIndicatorDto>());
    }
}
