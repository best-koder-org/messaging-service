namespace MessagingService.DTOs;

/// <summary>
/// Response DTO for a read receipt
/// </summary>
public record ReadReceiptDto(
    int MessageId,
    string ReaderId,
    DateTime ReadAt
);

/// <summary>
/// Request to mark a message as read
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
