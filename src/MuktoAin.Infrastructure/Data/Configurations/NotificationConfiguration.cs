using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("NOTIFICATION", "dbo");
        builder.HasKey(n => n.NotificationId);
        builder.HasIndex(n => new { n.UserId, n.IsRead });
        builder.HasIndex(n => n.CreatedAt);
        builder.Property(n => n.IsSeen).HasDefaultValue(false);

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.RelatedCase)
            .WithMany()
            .HasForeignKey(n => n.RelatedCaseId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(n => n.RelatedDocument)
            .WithMany()
            .HasForeignKey(n => n.RelatedDocumentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(n => n.RelatedLawyerProfile)
            .WithMany()
            .HasForeignKey(n => n.RelatedLawyerProfileId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
