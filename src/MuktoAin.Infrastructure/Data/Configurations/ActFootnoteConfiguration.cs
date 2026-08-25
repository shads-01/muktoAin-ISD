using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[ACT_FOOTNOTE] from scripts/02_schema.sql.
// PK property "FootnoteId" doesn't match EF's "<TypeName>Id" convention
// ("ActFootnoteId"), so it needs an explicit HasKey.
public class ActFootnoteConfiguration : IEntityTypeConfiguration<ActFootnote>
{
    public void Configure(EntityTypeBuilder<ActFootnote> builder)
    {
        builder.ToTable("ACT_FOOTNOTE", "dbo");
        builder.HasKey(f => f.FootnoteId);
    }
}
