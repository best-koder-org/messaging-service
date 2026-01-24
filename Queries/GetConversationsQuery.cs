using MediatR;
using MessagingService.Common;

namespace MessagingService.Queries;

public class GetConversationsQuery : IRequest<Result<object>>
{
    public string UserId { get; set; } = string.Empty;
}
