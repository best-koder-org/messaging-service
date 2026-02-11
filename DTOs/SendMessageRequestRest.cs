using System.ComponentModel.DataAnnotations;
using MessagingService.Models;

namespace MessagingService.DTOs;

public class SendMessageRequestRest
{
    [Required]
    public required string RecipientUserId { get; set; }

    [Required]
    [MaxLength(1000)]
    public required string Text { get; set; }

    public MessageType? Type { get; set; }
}
