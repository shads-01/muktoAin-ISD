using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[SCENARIO_MAPPING] from scripts/02_schema.sql.
// PK property "MappingId" doesn't match EF's "<TypeName>Id" convention
// ("ScenarioMappingId"), so it needs an explicit HasKey.
public class ScenarioMappingConfiguration : IEntityTypeConfiguration<ScenarioMapping>
{
    public void Configure(EntityTypeBuilder<ScenarioMapping> builder)
    {
        builder.ToTable("SCENARIO_MAPPING", "dbo");
        builder.HasKey(m => m.MappingId);
    }
}
