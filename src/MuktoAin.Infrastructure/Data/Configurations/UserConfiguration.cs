using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps the Identity-backed User entity onto the manually-authored [dbo].[USER]
// table from scripts/02_schema.sql. The physical PK column is named UserId
// (not Id) to stay consistent with every other table's <Entity>Id convention,
// so User.Id is remapped here via HasColumnName. Column lengths mirror the DDL.
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("USER", "dbo");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("UserId").ValueGeneratedOnAdd();

        // Domain fields (design.md §2.1)
        builder.Property(u => u.FullName).HasMaxLength(150).IsRequired();
        builder.Property(u => u.Role).IsRequired();
        builder.Property(u => u.AccountStatus).IsRequired().HasDefaultValue(AccountStatus.Active);
        builder.Property(u => u.PreferredLanguage).HasMaxLength(10).IsRequired().HasDefaultValue("bn");
        builder.Property(u => u.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasOne(u => u.CreatedByAdmin)
            .WithMany()
            .HasForeignKey(u => u.CreatedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        // ASP.NET Core Identity base columns — lengths match scripts/02_schema.sql
        builder.Property(u => u.UserName).HasMaxLength(256);
        builder.Property(u => u.NormalizedUserName).HasMaxLength(256);
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(256);

        builder.HasIndex(u => u.NormalizedUserName).IsUnique().HasFilter("[NormalizedUserName] IS NOT NULL");
        builder.HasIndex(u => u.NormalizedEmail);
    }
}
