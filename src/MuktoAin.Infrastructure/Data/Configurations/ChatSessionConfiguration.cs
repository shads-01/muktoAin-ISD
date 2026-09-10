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
        // Filtered: SessionKey is NULL for every logged-in user's session (only
        // guests get a real key — see ChatService.GetOrCreateSessionAsync), and
        // SQL Server's plain unique index allows only ONE NULL table-wide. The
        // filter excludes NULL rows from the uniqueness check so logged-in
        // sessions can't collide with each other while guest keys still can't.
        builder.HasIndex(s => s.SessionKey)
            .IsUnique()
            .HasFilter("[SessionKey] IS NOT NULL");
    }
}
