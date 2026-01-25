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
    private readonly ILogger<MessagingHubSpec> _logger;

    public MessagingHubSpec(
        IMessageServiceSpec messageService,
        IContentModerationService contentModeration,
        ILogger<MessagingHubSpec> logger)
    {
        _messageService = messageService;
        _contentModeration = contentModeration;
        _logger = logger;
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

            // Validate message length
            if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 1000)
            {
                throw new HubException("message-too-long");
            }

            // Content moderation
            var moderationResult = await _contentModeration.ModerateContentAsync(request.Body);
            if (!moderationResult.IsApproved)
            {
                _logger.LogWarning("Content blocked from user {SenderId} in match {MatchId}: {Reason}", 
                    senderId, request.MatchId, moderationResult.Reason);
                throw new HubException("content-blocked");
            }

            // Persist and broadcast message
            var messageDto = await _messageService.SendMessageAsync(request.MatchId, senderId, request.Body);

            // Get other participant
            var receiverId = await _messageService.GetOtherParticipant(request.MatchId, senderId);

            // Server-to-Client: MessageReceived for both participants
            await Clients.User(receiverId).SendAsync("MessageReceived", messageDto);
            await Clients.Caller.SendAsync("MessageReceived", messageDto);

            _logger.LogInformation("Message {MessageId} sent in match {MatchId}", 
                messageDto.MessageId, request.MatchId);
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
