using MediatR;
using MessagingService.Common;
using MessagingService.Models;

namespace MessagingService.Commands;

public class SendMessageCommand : IRequest<Result<MessageDto>>
{
    public required string SenderId { get; set; }
    public required string ReceiverId { get; set; }
    public required string Content { get; set; }
    public MessageType Type { get; set; } = MessageType.Text;
}

public class MessageDto
{
    public int Id { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public bool IsRead { get; set; }
    public MessageType Type { get; set; }
    public string ConversationId { get; set; } = string.Empty;
}
