using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class GeneratedDocumentConfiguration : IEntityTypeConfiguration<GeneratedDocument>
{
    public void Configure(EntityTypeBuilder<GeneratedDocument> builder)
    {
        builder.ToTable("GENERATED_DOCUMENT", "dbo");
        builder.HasKey(d => d.DocumentId);
        // Redesign columns (scripts/08_redesign_tables.sql) — additive
        builder.Property(d => d.VersionNo).HasDefaultValue(1);
        builder.Property(d => d.CitizenEdited).HasDefaultValue(false);
    }
}
