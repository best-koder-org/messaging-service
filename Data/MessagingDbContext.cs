using Microsoft.EntityFrameworkCore;
using MessagingService.Models;

namespace MessagingService.Data;

public class MessagingDbContext : DbContext
{
    public MessagingDbContext(DbContextOptions<MessagingDbContext> options) : base(options)
    {
    }

    public DbSet<Message> Messages { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SenderId).IsRequired().HasMaxLength(36);
            entity.Property(e => e.ReceiverId).IsRequired().HasMaxLength(36);
            entity.Property(e => e.Content).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.ConversationId).IsRequired().HasMaxLength(100);
            
            // Indexes for performance
            entity.HasIndex(e => e.SenderId);
            entity.HasIndex(e => e.ReceiverId);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.SentAt);
            entity.HasIndex(e => e.ModerationStatus);
        });
    }
}
