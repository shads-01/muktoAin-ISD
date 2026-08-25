using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[GENERATED_DOCUMENT] from scripts/02_schema.sql.
// PK property "DocumentId" doesn't match EF's "<TypeName>Id" convention
// ("GeneratedDocumentId"), so it needs an explicit HasKey.
public class GeneratedDocumentConfiguration : IEntityTypeConfiguration<GeneratedDocument>
{
    public void Configure(EntityTypeBuilder<GeneratedDocument> builder)
    {
        builder.ToTable("GENERATED_DOCUMENT", "dbo");
        builder.HasKey(d => d.DocumentId);
    }
}
