using MediatR;
using MessagingService.Common;
using MessagingService.Hubs;
using MessagingService.Services;
using Microsoft.AspNetCore.SignalR;

namespace MessagingService.Commands;

public class SendMessageHandler : IRequestHandler<SendMessageCommand, Result<MessageDto>>
{
    private readonly IMessageService _messageService;
    private readonly IHubContext<MessagingHubSpec>? _hubContext;
    private readonly ILogger<SendMessageHandler> _logger;

    public SendMessageHandler(
        IMessageService messageService,
        ILogger<SendMessageHandler> logger,
        IHubContext<MessagingHubSpec>? hubContext = null)
    {
        _messageService = messageService;
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task<Result<MessageDto>> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _messageService.SendMessageAsync(
                request.SenderId,
                request.ReceiverId,
                request.Content,
                request.Type,
                request.IsBotGenerated
            );

            var dto = new MessageDto
            {
                Id = message.Id,
                SenderId = message.SenderId,
                ReceiverId = message.ReceiverId,
                Content = message.Content,
                SentAt = message.SentAt,
                IsRead = message.IsRead,
                Type = message.Type,
                ConversationId = message.ConversationId
            };

            // Broadcast via SignalR so Flutter clients connected to MessagingHubSpec
            // receive the message in real-time. Without this, REST-sent messages
            // (e.g. from bot-service) only appear after a manual refresh.
            if (_hubContext != null)
            {
                try
                {
                    await _hubContext.Clients.User(request.ReceiverId)
                        .SendAsync("MessageReceived", dto, cancellationToken);
                    await _hubContext.Clients.User(request.SenderId)
                        .SendAsync("MessageReceived", dto, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "SignalR broadcast failed for message {MessageId} (delivery still persisted)",
                        message.Id);
                }
            }

            return Result<MessageDto>.Success(dto);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Unauthorized message attempt from {SenderId} to {ReceiverId}: {Message}",
                request.SenderId, request.ReceiverId, ex.Message);
            return Result<MessageDto>.Failure($"UNAUTHORIZED: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message from {SenderId} to {ReceiverId}",
                request.SenderId, request.ReceiverId);
            return Result<MessageDto>.Failure("Failed to send message");
        }
    }
}
