namespace MessagingService.DTOs;

public record TypingIndicatorDto
{
    public string UserId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public bool IsTyping { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
