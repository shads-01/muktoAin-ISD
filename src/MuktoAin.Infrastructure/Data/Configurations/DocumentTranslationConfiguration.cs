using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class DocumentTranslationConfiguration : IEntityTypeConfiguration<DocumentTranslation>
{
    public void Configure(EntityTypeBuilder<DocumentTranslation> builder)
    {
        builder.ToTable("DOCUMENT_TRANSLATION", "dbo");
        builder.HasKey(t => t.DocumentTranslationId);
        builder.HasIndex(t => new { t.DocumentId, t.Language }).IsUnique();
        builder.Property(t => t.Language).HasMaxLength(2).IsRequired();
        builder.Property(t => t.TranslatedContent).IsRequired();

        builder.HasOne(t => t.Document)
            .WithMany()
            .HasForeignKey(t => t.DocumentId);
    }
}
