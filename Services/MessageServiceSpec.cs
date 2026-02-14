using MessagingService.Data;
using MessagingService.DTOs;
using MessagingService.Models;
using Microsoft.EntityFrameworkCore;

namespace MessagingService.Services;

/// <summary>
/// Implementation of IMessageServiceSpec for SignalR messaging hub.
/// Handles match-based messaging with persistence and delivery tracking.
/// Uses int IDs matching MatchmakingService entity types — no Guid conversions.
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
    /// Check if a user is a participant in a match.
    /// Calls MatchmakingService to verify match ownership.
    /// </summary>
    public async Task<bool> IsMatchParticipant(int matchId, string userId)
    {
        try
        {
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

            return result.Matches.Any(m => m.MatchId == matchId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying match participant for {MatchId} and {UserId}", matchId, userId);
            return false;
        }
    }

    /// <summary>
    /// Send a message within a match and persist it.
    /// </summary>
    public async Task<MessageDto> SendMessageAsync(int matchId, string senderId, string body)
    {
        var receiverId = await GetOtherParticipant(matchId, senderId);

        if (string.IsNullOrEmpty(receiverId))
        {
            throw new InvalidOperationException("Cannot determine receiver for match");
        }

        var message = new Message
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Content = body,
            Type = MessageType.Text,
            ConversationId = matchId.ToString(),
            SentAt = DateTime.UtcNow,
            ModerationStatus = ModerationStatus.Approved,
            IsRead = false,
            IsDeleted = false
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Message {MessageId} sent in match {MatchId} from {SenderId} to {ReceiverId}",
            message.Id, matchId, senderId, receiverId);

        return new MessageDto
        {
            MessageId = message.Id,
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
    /// Get the other participant in a match (not the current user).
    /// </summary>
    public async Task<string> GetOtherParticipant(int matchId, string userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/matchmaking/matches/{userId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get match details for {MatchId}", matchId);
                return string.Empty;
            }

            var result = await response.Content.ReadFromJsonAsync<MatchesResponse>();

            if (result?.Matches == null)
                return string.Empty;

            var match = result.Matches.FirstOrDefault(m => m.MatchId == matchId);

            if (match == null)
                return string.Empty;

            return match.MatchedUserId.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting other participant for match {MatchId}", matchId);
            return string.Empty;
        }
    }

    /// <summary>
    /// Acknowledge message delivery (for delivery tracking).
    /// </summary>
    public async Task AcknowledgeMessageAsync(int messageId, string userId)
    {
        try
        {
            var message = await _context.Messages
                .FirstOrDefaultAsync(m => m.Id == messageId && m.ReceiverId == userId);

            if (message != null && !message.IsRead)
            {
                message.IsRead = true;
                message.ReadAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Message {MessageId} acknowledged by {UserId}", messageId, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acknowledging message {MessageId}", messageId);
        }
    }
}

// Helper DTOs for matchmaking API responses
record MatchesResponse(List<MatchDto> Matches, int TotalCount, int ActiveCount);
record MatchDto(int MatchId, int MatchedUserId, DateTime MatchedAt, double? CompatibilityScore);
