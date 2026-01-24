using MediatR;
using MessagingService.Common;
using MessagingService.Services;

namespace MessagingService.Queries;

public class GetConversationHandler : IRequestHandler<GetConversationQuery, Result<object>>
{
    private readonly IMessageService _messageService;
    private readonly ILogger<GetConversationHandler> _logger;

    public GetConversationHandler(IMessageService messageService, ILogger<GetConversationHandler> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    public async Task<Result<object>> Handle(GetConversationQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var messages = await _messageService.GetConversationAsync(
                request.UserId,
                request.OtherUserId,
                request.Page,
                request.PageSize);
            
            return Result<object>.Success(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversation between {UserId} and {OtherUserId}", 
                request.UserId, request.OtherUserId);
            return Result<object>.Failure("Failed to retrieve conversation");
        }
    }
}
