namespace MessagingService.DTOs;

/// <summary>
/// DTO for read receipt responses
/// </summary>
public record ReadReceiptDto(
    int MessageId,
    string ReaderId,
    DateTime ReadAt
);

/// <summary>
/// Request DTO for marking a message as read
/// </summary>
public record MarkAsReadRequest(int MessageId);

/// <summary>
/// Response DTO for unread message count
/// </summary>
public record UnreadCountDto(
    string ConversationId,
    int UnreadCount,
    DateTime? LastReadAt
);
