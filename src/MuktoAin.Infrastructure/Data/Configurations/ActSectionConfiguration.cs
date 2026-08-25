using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[ACT_SECTION] from scripts/02_schema.sql.
// PK property "SectionId" doesn't match EF's "<TypeName>Id" convention
// ("ActSectionId"), so it needs an explicit HasKey.
public class ActSectionConfiguration : IEntityTypeConfiguration<ActSection>
{
    public void Configure(EntityTypeBuilder<ActSection> builder)
    {
        builder.ToTable("ACT_SECTION", "dbo");
        builder.HasKey(s => s.SectionId);
    }
}
