namespace MessagingService.DTOs;

/// <summary>
/// DTO for read receipt responses
/// </summary>
public record ReadReceiptDto(
    Guid MessageId,
    string ReaderId,
    DateTime ReadAt
);

/// <summary>
/// Request DTO for marking a message as read
/// </summary>
public record MarkAsReadRequest(Guid MessageId);

/// <summary>
/// Response DTO for unread message count
/// </summary>
public record UnreadCountDto(
    string ConversationId,
    int UnreadCount,
    DateTime? LastReadAt
);
