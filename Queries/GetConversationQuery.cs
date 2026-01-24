using MediatR;
using MessagingService.Common;

namespace MessagingService.Queries;

public class GetConversationQuery : IRequest<Result<object>>
{
    public string UserId { get; set; } = string.Empty;
    public string OtherUserId { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
