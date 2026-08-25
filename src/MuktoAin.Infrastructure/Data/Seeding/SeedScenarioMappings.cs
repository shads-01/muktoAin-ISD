using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Loads hand-curated keyword-to-section mappings from data/scenario-mappings.json
// into SCENARIO_MAPPING.
//
// SectionId is a real FK to ACT_SECTION, which is only populated once the Acts
// import pipeline (T-1.8) has run. Until then ACT_SECTION is empty, so every
// mapping would violate the FK constraint if inserted as-is. Rather than crash
// startup or require a strict ordering, mappings whose SectionId isn't present yet
// are skipped with a warning. Because the "already seeded" idempotency check below
// only short-circuits once ScenarioMappings is non-empty, re-running this after
// T-1.8 completes will pick up the previously-skipped rows automatically.
public static class SeedScenarioMappings
{
    private sealed record ScenarioMappingSeedDto(int SectionId, string ScenarioKeyword, string? Notes);

    public static async Task SeedAsync(AppDbContext context, string contentRootPath, ILogger? logger = null)
    {
        if (await context.ScenarioMappings.AnyAsync()) return; // idempotent

        var dtos = await SeedJsonLoader.LoadAsync<ScenarioMappingSeedDto>(contentRootPath, "scenario-mappings.json");

        // Only check membership for the sections the JSON actually references,
        // rather than pulling every ACT_SECTION row into memory.
        var wantedSectionIds = dtos.Select(d => d.SectionId).Distinct().ToList();
        var existingSectionIds = (await context.ActSections
            .Where(s => wantedSectionIds.Contains(s.SectionId))
            .Select(s => s.SectionId)
            .ToListAsync())
            .ToHashSet();

        var toInsert = dtos.Where(d => existingSectionIds.Contains(d.SectionId)).ToList();
        var skipped = dtos.Count - toInsert.Count;
        if (skipped > 0)
        {
            logger?.LogWarning(
                "SeedScenarioMappings: skipped {Skipped} of {Total} mappings referencing sections not yet imported. " +
                "Re-run after the Acts import pipeline (T-1.8) completes.",
                skipped, dtos.Count);
        }

        if (toInsert.Count == 0) return;

        var mappings = toInsert.Select(d => new ScenarioMapping
        {
            SectionId = d.SectionId,
            ScenarioKeyword = d.ScenarioKeyword,
            Notes = d.Notes
        });
        context.ScenarioMappings.AddRange(mappings);
        await context.SaveChangesAsync();
    }
}
