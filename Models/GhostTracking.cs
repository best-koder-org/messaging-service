using System.ComponentModel.DataAnnotations;

namespace MessagingService.Models;

/// <summary>
/// Tracks ghost incidents that have already been reported to reputation-service.
/// Prevents duplicate penalties for the same conversation.
/// </summary>
public class GhostTracking
{
    [Key]
    public int Id { get; set; }

    /// <summary>The user who ghosted (stopped replying after mutual conversation).</summary>
    [Required, MaxLength(36)]
    public string GhostUserId { get; set; } = string.Empty;

    /// <summary>The user who was ghosted (left waiting for a reply).</summary>
    [Required, MaxLength(36)]
    public string VictimUserId { get; set; } = string.Empty;

    /// <summary>Conversation identifier (alphabetically-sorted user IDs).</summary>
    [Required, MaxLength(100)]
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>When the ghost incident was detected and reported.</summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the penalty was successfully sent to reputation-service.</summary>
    public bool Reported { get; set; }
}
