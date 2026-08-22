namespace MessagingService.Models;

/// <summary>
/// A reaction (heart "like") on a message, keyed by the reacting user's
/// Keycloak id. One row per (message, user) — a user can like a message once.
/// </summary>
public class MessageReaction
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Reaction { get; set; } = "like";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
