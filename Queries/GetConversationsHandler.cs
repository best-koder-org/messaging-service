using MediatR;
using MessagingService.Common;
using MessagingService.Services;

namespace MessagingService.Queries;

public class GetConversationsHandler : IRequestHandler<GetConversationsQuery, Result<object>>
{
    private readonly IMessageService _messageService;
    private readonly ILogger<GetConversationsHandler> _logger;

    public GetConversationsHandler(IMessageService messageService, ILogger<GetConversationsHandler> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    public async Task<Result<object>> Handle(GetConversationsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var conversations = await _messageService.GetConversationsAsync(request.UserId);
            return Result<object>.Success(conversations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversations for user {UserId}", request.UserId);
            return Result<object>.Failure("Failed to retrieve conversations");
        }
    }
}
