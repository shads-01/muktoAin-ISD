using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    public void Configure(EntityTypeBuilder<Case> builder)
    {
        builder.ToTable("CASE", "dbo");
        builder.HasKey(c => c.CaseId);
        // Redesign columns (scripts/08_redesign_tables.sql) — additive
        builder.Property(c => c.NotificationEmail).HasMaxLength(256);
        builder.Property(c => c.HasUnreadActivity).HasDefaultValue(false);
        builder.Property(c => c.HonorariumPaid).HasDefaultValue(false);
    }
}
