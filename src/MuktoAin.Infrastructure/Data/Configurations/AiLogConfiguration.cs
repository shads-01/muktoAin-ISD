using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[AI_LOG] from scripts/02_schema.sql.
// PK property "LogId" doesn't match EF's "<TypeName>Id" convention
// ("AiLogId"), so it needs an explicit HasKey.
public class AiLogConfiguration : IEntityTypeConfiguration<AiLog>
{
    public void Configure(EntityTypeBuilder<AiLog> builder)
    {
        builder.ToTable("AI_LOG", "dbo");
        builder.HasKey(l => l.LogId);
    }
}
