using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[LAWYER_PROFILE] from scripts/02_schema.sql.
// The FKs to [dbo].[USER] (UserId, VerifiedByAdminId) are left to Shads's
// ASP.NET Core Identity configuration (S-1.1) -- User isn't a DbSet here.
public class LawyerProfileConfiguration : IEntityTypeConfiguration<LawyerProfile>
{
    public void Configure(EntityTypeBuilder<LawyerProfile> builder)
    {
        builder.ToTable("LAWYER_PROFILE", "dbo");
        builder.HasKey(p => p.LawyerProfileId);

        // Redesign 2026-09 (scripts/09_part_b_tables.sql)
        builder.Property(p => p.RejectionReason).HasMaxLength(500);
    }
}
