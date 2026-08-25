using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[ACT] from scripts/02_schema.sql.
public class ActConfiguration : IEntityTypeConfiguration<Act>
{
    public void Configure(EntityTypeBuilder<Act> builder)
    {
        builder.ToTable("ACT", "dbo");
        builder.HasKey(a => a.ActId);
    }
}
