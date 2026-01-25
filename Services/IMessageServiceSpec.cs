using MessagingService.DTOs;

namespace MessagingService.Services;

/// <summary>
/// Message service specification interface for SignalR hub
/// Defines core messaging operations per signalr-spec.md
/// </summary>
public interface IMessageServiceSpec
{
    /// <summary>
    /// Check if a user is a participant in a match
    /// </summary>
    Task<bool> IsMatchParticipant(Guid matchId, string userId);

    /// <summary>
    /// Send a message within a match and persist it
    /// </summary>
    Task<MessageDto> SendMessageAsync(Guid matchId, string senderId, string body);

    /// <summary>
    /// Get the other participant in a match (not the current user)
    /// </summary>
    Task<string> GetOtherParticipant(Guid matchId, string userId);

    /// <summary>
    /// Acknowledge message delivery (for delivery tracking)
    /// </summary>
    Task AcknowledgeMessageAsync(Guid messageId, string userId);
}
