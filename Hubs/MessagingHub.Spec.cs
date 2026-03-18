using MessagingService.Metrics;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using MessagingService.Models;
using MessagingService.Services;
using MessagingService.DTOs;

namespace MessagingService.Hubs;

/// <summary>
/// SignalR Hub for real-time messaging aligned with signalr-spec.md
/// MMP Version: Basic send/receive only, no typing indicators or presence
/// </summary>
public class MessagingHubSpec : Hub
{
    private readonly IMessageServiceSpec _messageService;
    private readonly IContentModerationService _contentModeration;
    private readonly ISafetyAgentService _safetyAgent;
    private readonly ISafetyServiceClient _safetyService;
    private readonly ILogger<MessagingHubSpec> _logger;
    private readonly MessagingServiceMetrics? _metrics;

    public MessagingHubSpec(
        IMessageServiceSpec messageService,
        IContentModerationService contentModeration,
        ISafetyAgentService safetyAgent,
        ISafetyServiceClient safetyService,
        ILogger<MessagingHubSpec> logger,
        MessagingServiceMetrics? metrics = null)
    {
        _messageService = messageService;
        _contentModeration = contentModeration;
        _safetyAgent = safetyAgent;
        _safetyService = safetyService;
        _logger = logger;
        _metrics = metrics;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();

        if (string.IsNullOrEmpty(userId))
        {
            Context.Abort();
            return;
        }

        _logger.LogInformation("User {UserId} connected with connection {ConnectionId}",
            userId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();

        if (!string.IsNullOrEmpty(userId))
        {
            _logger.LogInformation("User {UserId} disconnected", userId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client-to-Server: Send message to match participants
    /// Payload: { matchId: guid, body: string }
    /// </summary>
    public async Task SendMessage(SendMessageRequest request)
    {
        var senderId = GetUserId();

        if (string.IsNullOrEmpty(senderId))
        {
            throw new HubException("authentication-required");
        }

        try
        {
            // Validate match ownership
            var isParticipant = await _messageService.IsMatchParticipant(request.MatchId, senderId);
            if (!isParticipant)
            {
                throw new HubException("not-authorized");
            }

            // Get other participant
            var receiverId = await _messageService.GetOtherParticipant(request.MatchId, senderId);

            // Check if users have blocked each other (P0 safety requirement)
            var isBlocked = await _safetyService.IsBlockedAsync(senderId, receiverId);
            var isBlockedReverse = await _safetyService.IsBlockedAsync(receiverId, senderId);

            if (isBlocked || isBlockedReverse)
            {
                _logger.LogWarning("Messaging blocked between users {SenderId} and {ReceiverId} in match {MatchId}",
                    senderId, receiverId, request.MatchId);
                throw new HubException("messaging-blocked");
            }

            // Validate message length
            if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 1000)
            {
                throw new HubException("message-too-long");
            }

            var isAudio = string.Equals(request.BodyType, "Audio", StringComparison.OrdinalIgnoreCase);
            string? moderationFlag = null;

            // AI Safety Agent classification — skip for audio (content is a URL, not text)
            if (!isAudio)
            {
                var safety = await _safetyAgent.ClassifyAsync(request.Body);

                if (safety.Level == SafetyLevel.Block)
                {
                    _logger.LogWarning("Message BLOCKED from {SenderId} in match {MatchId}: {Reason} (confidence: {Confidence:P0})",
                        senderId, request.MatchId, safety.Reason, safety.Confidence);
                    _metrics?.MessageModerated();
                    throw new HubException("content-blocked");
                }

                if (safety.Level == SafetyLevel.Warning)
                {
                    moderationFlag = safety.Reason;
                    _logger.LogInformation("Message WARNING from {SenderId} in match {MatchId}: {Reason}",
                        senderId, request.MatchId, safety.Reason);
                }
            }

            // Persist and broadcast message
            var messageDto = await _messageService.SendMessageAsync(
                request.MatchId, senderId, request.Body, request.BodyType, request.AudioDurationSeconds);

            // Attach safety metadata for warned messages
            if (moderationFlag != null)
            {
                messageDto.ModerationFlag = moderationFlag;
            }

            // Server-to-Client: MessageReceived for both participants
            await Clients.User(receiverId).SendAsync("MessageReceived", messageDto);
            await Clients.Caller.SendAsync("MessageReceived", messageDto);

            _logger.LogInformation("Message {MessageId} sent in match {MatchId}",
                messageDto.MessageId, request.MatchId);

            _metrics?.MessageSent();
        }
        catch (HubException)
        {
            throw; // Re-throw HubExceptions as-is
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message in match {MatchId}", request.MatchId);
            throw new HubException("send-failed");
        }
    }

    /// <summary>
    /// Client-to-Server: Acknowledge message reception for delivery tracking
    /// Payload: { messageId: guid }
    /// Note: Basic MMP implementation, full read receipts deferred
    /// </summary>
    public async Task Acknowledge(AcknowledgeRequest request)
    {
        var userId = GetUserId();

        if (string.IsNullOrEmpty(userId))
        {
            throw new HubException("authentication-required");
        }

        try
        {
            await _messageService.AcknowledgeMessageAsync(request.MessageId, userId);

            _logger.LogInformation("Message {MessageId} acknowledged by {UserId}",
                request.MessageId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acknowledging message {MessageId}", request.MessageId);
            // Don't throw - acknowledgment failures shouldn't break UX
        }
    }

    private string? GetUserId()
    {
        return Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;
    }
}
