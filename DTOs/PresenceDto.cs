namespace MessagingService.DTOs;

/// <summary>
/// DTO for user presence status in SignalR messaging.
/// Used to notify clients when users come online/go offline.
/// </summary>
public class PresenceDto
{
    public string UserId { get; set; } = string.Empty;
    public string State { get; set; } = "Offline"; // "Online" or "Offline"
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response containing a list of online users.
/// </summary>
public class OnlineUsersResponse
{
    public IReadOnlyList<string> OnlineUserIds { get; set; } = Array.Empty<string>();
    public int Count { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
