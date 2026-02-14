using MessagingService.DTOs;

namespace MessagingService.Services;

/// <summary>
/// Message service specification interface for SignalR hub.
/// Uses int IDs matching MatchmakingService entity types.
/// </summary>
public interface IMessageServiceSpec
{
    /// <summary>
    /// Check if a user is a participant in a match
    /// </summary>
    Task<bool> IsMatchParticipant(int matchId, string userId);

    /// <summary>
    /// Send a message within a match and persist it
    /// </summary>
    Task<MessageDto> SendMessageAsync(int matchId, string senderId, string body);

    /// <summary>
    /// Get the other participant in a match (not the current user)
    /// </summary>
    Task<string> GetOtherParticipant(int matchId, string userId);

    /// <summary>
    /// Acknowledge message delivery (for delivery tracking)
    /// </summary>
    Task AcknowledgeMessageAsync(int messageId, string userId);
}
