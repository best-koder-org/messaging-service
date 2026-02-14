using System.ComponentModel.DataAnnotations;

namespace MessagingService.DTOs;

public class SendMessageRequest
{
    public int MatchId { get; set; }
    public string Body { get; set; } = string.Empty;
}

public class AcknowledgeRequest
{
    public int MessageId { get; set; }
}

public class MessageDto
{
    public int MessageId { get; set; }
    public int MatchId { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string BodyType { get; set; } = "Text";
    public DateTime SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public string? ModerationFlag { get; set; }
}
