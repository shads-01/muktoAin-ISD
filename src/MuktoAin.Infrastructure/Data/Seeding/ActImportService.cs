using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Imports the ~1,484 Bangladesh Acts from data/bangladesh-acts-dataset.json into
// ACT -> ACT_SECTION -> ACT_FOOTNOTE (T-1.8). See data/README.md for the dataset's
// provenance and its pinned SHA256.
//
// The source has no separate "section number" field -- section text is scraped
// with footnote-reference markers (e.g. "the2[***] Government") flattened inline,
// which makes leading-digit text look like a section number when it sometimes
// isn't. SectionNumber is left null by design; ordering relies on OrdinalPosition.
public static class ActImportService
{
    private sealed record ActsDatasetDto(
        [property: JsonPropertyName("acts")] List<ActSeedDto> Acts);

    private sealed record ActSeedDto(
        [property: JsonPropertyName("act_title")] string ActTitle,
        [property: JsonPropertyName("act_no")] string? ActNo,
        [property: JsonPropertyName("act_year")] string ActYear,
        [property: JsonPropertyName("publication_date")] string? PublicationDate,
        [property: JsonPropertyName("sections")] List<ActSectionSeedDto>? Sections,
        [property: JsonPropertyName("footnotes")] List<ActFootnoteSeedDto>? Footnotes,
        [property: JsonPropertyName("source_url")] string? SourceUrl,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("token_count")] int TokenCount,
        [property: JsonPropertyName("csv_metadata")] CsvMetadataDto? CsvMetadata);

    private sealed record ActSectionSeedDto(
        [property: JsonPropertyName("section_title")] string? SectionTitle,
        [property: JsonPropertyName("section_content")] string? SectionContent);

    private sealed record ActFootnoteSeedDto(
        [property: JsonPropertyName("footnote_text")] string? FootnoteText);

    private sealed record CsvMetadataDto(
        [property: JsonPropertyName("is_repealed")] bool IsRepealed);

    private const string FileName = "bangladesh-acts-dataset.json";

    public static async Task SeedAsync(AppDbContext context, string contentRootPath, ILogger? logger = null)
    {
        // This file is large and git-ignored (see data/README.md) -- a teammate
        // not working on Acts/RAG data may not have downloaded it. Skip rather
        // than crash startup for everyone else.
        if (!SeedDataPathResolver.TryResolve(contentRootPath, FileName, out _))
        {
            logger?.LogWarning(
                "ActImportService: '{FileName}' not found -- skipping Acts import. " +
                "Download it from Kaggle (see data/README.md) to populate ACT/ACT_SECTION/ACT_FOOTNOTE.",
                FileName);
            return;
        }

        var dataset = await SeedJsonLoader.LoadObjectAsync<ActsDatasetDto>(contentRootPath, FileName);

        // Idempotent per-act (Title+Year), not just "table is empty" -- an
        // interrupted run can resume without re-importing what already landed.
        var existingKeys = (await context.Acts
            .Select(a => new { a.Title, a.Year })
            .ToListAsync())
            .Select(a => (a.Title, a.Year))
            .ToHashSet();

        var imported = 0;
        var skippedExisting = 0;
        var skippedBadYear = 0;

        foreach (var dto in dataset.Acts)
        {
            if (!int.TryParse(dto.ActYear, out var year))
            {
                skippedBadYear++;
                continue;
            }

            if (existingKeys.Contains((dto.ActTitle, year)))
            {
                skippedExisting++;
                continue;
            }

            var act = new Act
            {
                Title = dto.ActTitle,
                ActNumber = dto.ActNo ?? string.Empty,
                Year = year,
                PublicationDate = dto.PublicationDate ?? string.Empty,
                Language = dto.Language ?? "unknown",
                IsRepealed = dto.CsvMetadata?.IsRepealed ?? false,
                TokenCount = dto.TokenCount,
                SourceUrl = dto.SourceUrl ?? string.Empty,
                ImportedAt = DateTime.UtcNow
            };

            var ordinal = 1;
            foreach (var section in dto.Sections ?? [])
            {
                act.Sections.Add(new ActSection
                {
                    OrdinalPosition = ordinal++,
                    SectionTitle = section.SectionTitle,
                    SectionText = section.SectionContent ?? string.Empty
                });
            }

            var footnoteOrder = 1;
            foreach (var footnote in dto.Footnotes ?? [])
            {
                act.Footnotes.Add(new ActFootnote
                {
                    FootnoteOrder = footnoteOrder++,
                    FootnoteText = footnote.FootnoteText ?? string.Empty
                });
            }

            context.Acts.Add(act);
            await context.SaveChangesAsync();
            imported++;

            if (imported % 100 == 0)
            {
                logger?.LogInformation("ActImportService: imported {Imported} acts so far...", imported);
            }
        }

        logger?.LogInformation(
            "ActImportService: imported {Imported} new acts, skipped {SkippedExisting} already present, " +
            "skipped {SkippedBadYear} with an unparsable act_year.",
            imported, skippedExisting, skippedBadYear);
    }
}
