using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class AnswerCacheConfiguration : IEntityTypeConfiguration<AnswerCache>
{
    public void Configure(EntityTypeBuilder<AnswerCache> builder)
    {
        builder.ToTable("ANSWER_CACHE", "dbo");
        builder.HasKey(a => a.AnswerCacheId);
        builder.HasIndex(a => a.QueryHash).IsUnique();
    }
}
