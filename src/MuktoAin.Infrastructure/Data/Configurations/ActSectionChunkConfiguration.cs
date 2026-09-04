using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[ACT_SECTION_CHUNK] from scripts/02_schema.sql.
// PK property "ChunkId" doesn't match EF's "<TypeName>Id" convention
// ("ActSectionChunkId"), so it needs an explicit HasKey.
public class ActSectionChunkConfiguration : IEntityTypeConfiguration<ActSectionChunk>
{
    public void Configure(EntityTypeBuilder<ActSectionChunk> builder)
    {
        builder.ToTable("ACT_SECTION_CHUNK", "dbo");
        builder.HasKey(c => c.ChunkId);

        builder.HasIndex(c => c.SectionId)
            .HasDatabaseName("IX_ACT_SECTION_CHUNK_SectionId");

        builder.HasIndex(c => new { c.VectorId, c.ChunkId })
            .HasDatabaseName("IX_ACT_SECTION_CHUNK_VectorId");
    }
}
