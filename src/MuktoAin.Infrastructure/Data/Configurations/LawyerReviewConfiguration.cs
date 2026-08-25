using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[LAWYER_REVIEW] from scripts/02_schema.sql.
// PK property "ReviewId" doesn't match EF's "<TypeName>Id" convention
// ("LawyerReviewId"), so it needs an explicit HasKey.
public class LawyerReviewConfiguration : IEntityTypeConfiguration<LawyerReview>
{
    public void Configure(EntityTypeBuilder<LawyerReview> builder)
    {
        builder.ToTable("LAWYER_REVIEW", "dbo");
        builder.HasKey(r => r.ReviewId);
    }
}
