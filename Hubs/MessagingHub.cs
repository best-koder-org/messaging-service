using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using MessagingService.Models;
using MessagingService.Services;

namespace MessagingService.Hubs;

public class MessagingHub : Hub
{
    private readonly IMessageService _messageService;
    private readonly IContentModerationService _contentModeration;
    private readonly ISpamDetectionService _spamDetection;
    private readonly IRateLimitingService _rateLimiting;
    private readonly IReportingService _reportingService;
    private readonly ILogger<MessagingHub> _logger;

    public MessagingHub(
        IMessageService messageService,
        IContentModerationService contentModeration,
        ISpamDetectionService spamDetection,
        IRateLimitingService rateLimiting,
        IReportingService reportingService,
        ILogger<MessagingHub> logger)
    {
        _messageService = messageService;
        _contentModeration = contentModeration;
        _spamDetection = spamDetection;
        _rateLimiting = rateLimiting;
        _reportingService = reportingService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId))
        {
            Context.Abort();
            return;
        }
        
        await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        _logger.LogInformation($"User {userId} connected with connection {Context.ConnectionId}");
        
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
            _logger.LogInformation($"User {userId} disconnected");
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(string receiverId, string content, MessageType type = MessageType.Text)
    {
        var senderId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(senderId))
        {
            await Clients.Caller.SendAsync("Error", "Authentication required");
            return;
        }

        try
        {
            // Check if user is banned
            if (await _reportingService.IsUserBannedAsync(senderId))
            {
                await Clients.Caller.SendAsync("Error", "You are temporarily banned from sending messages");
                return;
            }

            // Rate limiting check
            if (!await _rateLimiting.IsAllowedAsync(senderId))
            {
                await Clients.Caller.SendAsync("Error", "Rate limit exceeded. Please wait before sending another message.");
                return;
            }

            // Spam detection
            if (await _spamDetection.IsSpamAsync(senderId, content))
            {
                await Clients.Caller.SendAsync("Error", "Message flagged as potential spam");
                _logger.LogWarning($"Spam detected from user {senderId}: {content}");
                return;
            }

            // Content moderation
            var moderationResult = await _contentModeration.ModerateContentAsync(content);
            if (!moderationResult.IsApproved)
            {
                await Clients.Caller.SendAsync("Error", $"Message blocked: {moderationResult.Reason}");
                _logger.LogWarning($"Content blocked from user {senderId}: {moderationResult.Reason}");
                return;
            }

            // Send message
            var message = await _messageService.SendMessageAsync(senderId, receiverId, content, type);
            
            // Send to both users
            await Clients.Group(receiverId).SendAsync("ReceiveMessage", new
            {
                message.Id,
                message.SenderId,
                message.ReceiverId,
                message.Content,
                message.SentAt,
                message.Type,
                message.ConversationId
            });

            await Clients.Caller.SendAsync("MessageSent", new
            {
                message.Id,
                message.SenderId,
                message.ReceiverId,
                message.Content,
                message.SentAt,
                message.Type,
                message.ConversationId
            });

            _logger.LogInformation($"Message sent from {senderId} to {receiverId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error sending message from {senderId} to {receiverId}");
            await Clients.Caller.SendAsync("Error", "Failed to send message");
        }
    }

    public async Task MarkAsRead(int messageId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId))
        {
            await Clients.Caller.SendAsync("Error", "Authentication required");
            return;
        }

        try
        {
            await _messageService.MarkAsReadAsync(messageId, userId);
            await Clients.Caller.SendAsync("MessageRead", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error marking message {messageId} as read for user {userId}");
            await Clients.Caller.SendAsync("Error", "Failed to mark message as read");
        }
    }

    public async Task JoinConversation(string conversationId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
        _logger.LogInformation($"User {userId} joined conversation {conversationId}");
    }

    public async Task LeaveConversation(string conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
        
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _logger.LogInformation($"User {userId} left conversation {conversationId}");
    }
}
