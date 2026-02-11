using System.ComponentModel.DataAnnotations;

namespace MessagingService.Models;

public class Message
{
    public int Id { get; set; }

    [Required]
    public string SenderId { get; set; } = string.Empty;

    [Required]
    public string ReceiverId { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Content { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAt { get; set; }

    public bool IsRead { get; set; }

    public bool IsDeleted { get; set; }

    public MessageType Type { get; set; } = MessageType.Text;

    // Safety Features
    public bool IsFlagged { get; set; }
    public string? FlagReason { get; set; }
    public ModerationStatus ModerationStatus { get; set; } = ModerationStatus.Pending;
    public DateTime? ModeratedAt { get; set; }
    public string? ModeratedBy { get; set; }

    // Conversation tracking
    public string ConversationId { get; set; } = string.Empty;
}

public enum MessageType
{
    Text = 0,
    Image = 1,
    Emoji = 2
}

public enum ModerationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    RequiresReview = 3
}
