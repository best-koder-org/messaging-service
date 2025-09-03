using MessagingService.Models;
using MessagingService.Data;
using Microsoft.EntityFrameworkCore;

namespace MessagingService.Services;

public interface IMessageService
{
    Task<Message> SendMessageAsync(string senderId, string receiverId, string content, MessageType type = MessageType.Text);
    Task<List<Message>> GetConversationAsync(string userId, string otherUserId, int page = 1, int pageSize = 50);
    Task<List<ConversationSummary>> GetConversationsAsync(string userId);
    Task<Message?> GetMessageAsync(int messageId);
    Task MarkAsReadAsync(int messageId, string userId);
    Task<bool> DeleteMessageAsync(int messageId, string userId);
}

public class MessageService : IMessageService
{
    private readonly MessagingDbContext _context;
    private readonly ILogger<MessageService> _logger;

    public MessageService(MessagingDbContext context, ILogger<MessageService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Message> SendMessageAsync(string senderId, string receiverId, string content, MessageType type = MessageType.Text)
    {
        var conversationId = GenerateConversationId(senderId, receiverId);
        
        var message = new Message
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Content = content,
            Type = type,
            ConversationId = conversationId,
            SentAt = DateTime.UtcNow,
            ModerationStatus = ModerationStatus.Approved // Already moderated in hub
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        _logger.LogInformation($"Message saved: {message.Id} from {senderId} to {receiverId}");
        return message;
    }

    public async Task<List<Message>> GetConversationAsync(string userId, string otherUserId, int page = 1, int pageSize = 50)
    {
        var conversationId = GenerateConversationId(userId, otherUserId);
        
        return await _context.Messages
            .Where(m => m.ConversationId == conversationId && 
                       !m.IsDeleted && 
                       m.ModerationStatus == ModerationStatus.Approved)
            .OrderByDescending(m => m.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<ConversationSummary>> GetConversationsAsync(string userId)
    {
        var conversations = await _context.Messages
            .Where(m => (m.SenderId == userId || m.ReceiverId == userId) && 
                       !m.IsDeleted && 
                       m.ModerationStatus == ModerationStatus.Approved)
            .GroupBy(m => m.ConversationId)
            .Select(g => new ConversationSummary
            {
                ConversationId = g.Key,
                LastMessage = g.OrderByDescending(m => m.SentAt).First(),
                UnreadCount = g.Count(m => m.ReceiverId == userId && !m.IsRead),
                OtherUserId = g.First().SenderId == userId ? g.First().ReceiverId : g.First().SenderId
            })
            .OrderByDescending(c => c.LastMessage.SentAt)
            .ToListAsync();

        return conversations;
    }

    public async Task<Message?> GetMessageAsync(int messageId)
    {
        return await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && !m.IsDeleted);
    }

    public async Task MarkAsReadAsync(int messageId, string userId)
    {
        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ReceiverId == userId);

        if (message != null && !message.IsRead)
        {
            message.IsRead = true;
            message.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> DeleteMessageAsync(int messageId, string userId)
    {
        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.SenderId == userId);

        if (message != null)
        {
            message.IsDeleted = true;
            await _context.SaveChangesAsync();
            return true;
        }

        return false;
    }

    private static string GenerateConversationId(string userId1, string userId2)
    {
        // Always order user IDs consistently to generate same conversation ID
        var users = new[] { userId1, userId2 }.OrderBy(x => x).ToArray();
        return $"{users[0]}_{users[1]}";
    }
}

public class ConversationSummary
{
    public string ConversationId { get; set; } = string.Empty;
    public Message LastMessage { get; set; } = null!;
    public int UnreadCount { get; set; }
    public string OtherUserId { get; set; } = string.Empty;
}
