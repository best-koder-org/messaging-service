namespace MessagingService.Services;

/// <summary>
/// Tracks user online/offline presence via SignalR connection management.
/// Designed for single-server deployment; upgrade to Redis-backed when scaling.
/// </summary>
public interface IPresenceTracker
{
    /// <summary>
    /// Register a new SignalR connection for a user.
    /// Returns true if this is the user's first connection (just came online).
    /// </summary>
    Task<bool> UserConnected(string userId, string connectionId);

    /// <summary>
    /// Remove a SignalR connection for a user.
    /// Returns true if this was the user's last connection (went offline).
    /// </summary>
    Task<bool> UserDisconnected(string userId, string connectionId);

    /// <summary>
    /// Get the list of currently online user IDs.
    /// </summary>
    Task<IReadOnlyList<string>> GetOnlineUsers();

    /// <summary>
    /// Check if a specific user has any active connections.
    /// </summary>
    Task<bool> IsOnline(string userId);

    /// <summary>
    /// Get all active connection IDs for a specific user.
    /// </summary>
    Task<IReadOnlyList<string>> GetConnectionsForUser(string userId);
}
