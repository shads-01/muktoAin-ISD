using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[ADMIN_AUDIT_LOG] from scripts/14_add_admin_audit_log.sql (AUD-7).
// AdminUser FK mirrors LawyerProfileConfiguration's FK-to-USER pattern; TargetUserId
// is intentionally NOT configured as an EF relationship (it is a soft reference —
// the audit row must survive even if the target user is ever hard-deleted).
public class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminAuditLog> builder)
    {
        builder.ToTable("ADMIN_AUDIT_LOG", "dbo");
        builder.HasKey(l => l.AdminAuditLogId);
        builder.Property(l => l.Action).IsRequired().HasMaxLength(50);
        builder.Property(l => l.Details).HasMaxLength(1000);
        builder.HasOne(l => l.AdminUser)
               .WithMany()
               .HasForeignKey(l => l.AdminUserId);
    }
}
