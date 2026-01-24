using MediatR;
using MessagingService.Common;

namespace MessagingService.Commands;

public class DeleteMessageCommand : IRequest<Result>
{
    public int MessageId { get; set; }
    public string UserId { get; set; } = string.Empty;
}
