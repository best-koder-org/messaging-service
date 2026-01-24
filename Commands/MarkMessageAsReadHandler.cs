using MediatR;
using MessagingService.Common;
using MessagingService.Services;

namespace MessagingService.Commands;

public class MarkMessageAsReadHandler : IRequestHandler<MarkMessageAsReadCommand, Result>
{
    private readonly IMessageService _messageService;
    private readonly ILogger<MarkMessageAsReadHandler> _logger;

    public MarkMessageAsReadHandler(IMessageService messageService, ILogger<MarkMessageAsReadHandler> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkMessageAsReadCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await _messageService.MarkAsReadAsync(request.MessageId, request.UserId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking message {MessageId} as read for user {UserId}", 
                request.MessageId, request.UserId);
            return Result.Failure("Failed to mark message as read");
        }
    }
}
