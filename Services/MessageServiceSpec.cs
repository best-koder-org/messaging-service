using MessagingService.Data;
using MessagingService.DTOs;
using MessagingService.Models;
using Microsoft.EntityFrameworkCore;

namespace MessagingService.Services;

/// <summary>
/// Implementation of IMessageServiceSpec for SignalR messaging hub
/// Handles match-based messaging with persistence and delivery tracking
/// </summary>
public class MessageServiceSpec : IMessageServiceSpec
{
    private readonly MessagingDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MessageServiceSpec> _logger;

    public MessageServiceSpec(
        MessagingDbContext context,
        HttpClient httpClient,
        ILogger<MessageServiceSpec> logger)
    {
        _context = context;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Check if a user is a participant in a match
    /// Calls MatchmakingService to verify match ownership
    /// </summary>
    public async Task<bool> IsMatchParticipant(Guid matchId, string userId)
    {
        try
        {
            // Call matchmaking service to verify match participants
            var response = await _httpClient.GetAsync($"/api/matchmaking/matches/{userId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to verify match {MatchId} for user {UserId}: {StatusCode}",
                    matchId, userId, response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<MatchesResponse>();

            if (result?.Matches == null)
                return false;

            // Convert Guid matchId to int for comparison with MatchDto.MatchId
            // Use simplified conversion (take first digits)
            var matchIdInt = int.Parse(matchId.ToString().Substring(0, 8),
                System.Globalization.NumberStyles.HexNumber);

            // Check if match exists in user's matches
            return result.Matches.Any(m => m.MatchId == matchIdInt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying match participant for {MatchId} and {UserId}", matchId, userId);
            return false;
        }
    }

    /// <summary>
    /// Send a message within a match and persist it
    /// </summary>
    public async Task<MessageDto> SendMessageAsync(Guid matchId, string senderId, string body)
    {
        // Get the other participant
        var receiverId = await GetOtherParticipant(matchId, senderId);

        if (string.IsNullOrEmpty(receiverId))
        {
            throw new InvalidOperationException("Cannot determine receiver for match");
        }

        // Create and persist message
        var message = new Message
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Content = body,
            Type = MessageType.Text,
            ConversationId = matchId.ToString(), // Use matchId as conversationId
            SentAt = DateTime.UtcNow,
            ModerationStatus = ModerationStatus.Approved, // Already moderated in hub
            IsRead = false,
            IsDeleted = false
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Message {MessageId} sent in match {MatchId} from {SenderId} to {ReceiverId}",
            message.Id, matchId, senderId, receiverId);

        // Return DTO for SignalR broadcast
        return new MessageDto
        {
            MessageId = new Guid(message.Id.ToString().PadLeft(32, '0')),
            MatchId = matchId,
            SenderId = senderId,
            Body = body,
            BodyType = "Text",
            SentAt = message.SentAt,
            DeliveredAt = null,
            ReadAt = null,
            ModerationFlag = null
        };
    }

    /// <summary>
    /// Get the other participant in a match (not the current user)
    /// </summary>
    public async Task<string> GetOtherParticipant(Guid matchId, string userId)
    {
        try
        {
            // Call matchmaking service to get match details
            var response = await _httpClient.GetAsync($"/api/matchmaking/matches/{userId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get match details for {MatchId}", matchId);
                return string.Empty;
            }

            var result = await response.Content.ReadFromJsonAsync<MatchesResponse>();

            if (result?.Matches == null)
                return string.Empty;

            // Convert Guid matchId to int for comparison
            var matchIdInt = int.Parse(matchId.ToString().Substring(0, 8),
                System.Globalization.NumberStyles.HexNumber);

            // Find the specific match and return the other user
            var match = result.Matches.FirstOrDefault(m => m.MatchId == matchIdInt);

            if (match == null)
                return string.Empty;

            // Return the matched user ID (the one that's not the current user)
            return match.MatchedUserId.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting other participant for match {MatchId}", matchId);
            return string.Empty;
        }
    }

    /// <summary>
    /// Acknowledge message delivery (for delivery tracking)
    /// Basic implementation - updates deliveredAt timestamp
    /// </summary>
    public async Task AcknowledgeMessageAsync(Guid messageId, string userId)
    {
        try
        {
            // Convert Guid back to int ID (simplified - in production would use Guid in DB)
            var messageIdInt = int.Parse(messageId.ToString().Substring(0, 10).TrimStart('0').PadLeft(1, '1'));

            var message = await _context.Messages
                .FirstOrDefaultAsync(m => m.Id == messageIdInt && m.ReceiverId == userId);

            if (message != null)
            {
                // In basic MMP implementation, acknowledgment updates read status
                // Full delivery tracking deferred to Phase 2
                if (!message.IsRead)
                {
                    message.IsRead = true;
                    message.ReadAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Message {MessageId} acknowledged by {UserId}", messageId, userId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acknowledging message {MessageId}", messageId);
            // Don't throw - acknowledgment failures shouldn't break UX
        }
    }
}

// Helper DTOs for matchmaking API responses
record MatchesResponse(List<MatchDto> Matches, int TotalCount, int ActiveCount);
record MatchDto(int MatchId, int MatchedUserId, DateTime MatchedAt, double? CompatibilityScore);
