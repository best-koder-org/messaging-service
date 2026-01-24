using MediatR;
using MessagingService.Common;
using MessagingService.Services;

namespace MessagingService.Commands;

public class DeleteMessageHandler : IRequestHandler<DeleteMessageCommand, Result>
{
    private readonly IMessageService _messageService;
    private readonly ILogger<DeleteMessageHandler> _logger;

    public DeleteMessageHandler(IMessageService messageService, ILogger<DeleteMessageHandler> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    public async Task<Result> Handle(DeleteMessageCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var success = await _messageService.DeleteMessageAsync(request.MessageId, request.UserId);
            
            if (!success)
            {
                return Result.Failure("Message not found or you don't have permission to delete it");
            }
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting message {MessageId} for user {UserId}", 
                request.MessageId, request.UserId);
            return Result.Failure("Failed to delete message");
        }
    }
}
