using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable("CHAT_SESSION", "dbo");
        builder.HasKey(s => s.ChatSessionId);
        builder.HasIndex(s => s.SessionKey)
            .HasDatabaseName("IX_CHAT_SESSION_SessionKey")
            .HasFilter("[SessionKey] IS NOT NULL");
        builder.Property(s => s.BlockedStreak).HasDefaultValue(0);
    }
}
