using MediatR;
using MessagingService.Common;
using MessagingService.Services;

namespace MessagingService.Commands;

public class SendMessageHandler : IRequestHandler<SendMessageCommand, Result<MessageDto>>
{
    private readonly IMessageService _messageService;
    private readonly ILogger<SendMessageHandler> _logger;

    public SendMessageHandler(IMessageService messageService, ILogger<SendMessageHandler> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    public async Task<Result<MessageDto>> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _messageService.SendMessageAsync(
                request.SenderId,
                request.ReceiverId,
                request.Content,
                request.Type
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
