using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[CASE_ACT_REFERENCE] from scripts/02_schema.sql.
public class CaseActReferenceConfiguration : IEntityTypeConfiguration<CaseActReference>
{
    public void Configure(EntityTypeBuilder<CaseActReference> builder)
    {
        builder.ToTable("CASE_ACT_REFERENCE", "dbo");
        builder.HasKey(r => r.CaseActReferenceId);

        // DECIMAL(5,4) in the SSMS schema; EF's decimal default (18,2) would
        // silently truncate scores like 0.9231 without this.
        builder.Property(r => r.RelevanceScore).HasPrecision(5, 4);
    }
}
