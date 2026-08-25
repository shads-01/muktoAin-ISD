using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[CASE_CATEGORY] from scripts/02_schema.sql.
// PK property "CategoryId" doesn't match EF's "<TypeName>Id" convention
// ("CaseCategoryId"), so it needs an explicit HasKey.
public class CaseCategoryConfiguration : IEntityTypeConfiguration<CaseCategory>
{
    public void Configure(EntityTypeBuilder<CaseCategory> builder)
    {
        builder.ToTable("CASE_CATEGORY", "dbo");
        builder.HasKey(c => c.CategoryId);
    }
}
