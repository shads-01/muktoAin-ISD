using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[CASE] from scripts/02_schema.sql.
// CASE is a T-SQL reserved word (see AGENTS.md / Tultul_plan.md Step 1.6) --
// EF Core quotes the identifier in generated SQL regardless, but the explicit
// ToTable keeps this in lockstep with the SSMS script rather than relying on
// DbSet<Case> pluralizing to "Cases".
// The FK to [dbo].[USER] (UserId) is left to Shads's Identity configuration
// (S-1.1) -- User isn't a DbSet here.
public class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    public void Configure(EntityTypeBuilder<Case> builder)
    {
        builder.ToTable("CASE", "dbo");
        builder.HasKey(c => c.CaseId);
    }
}
