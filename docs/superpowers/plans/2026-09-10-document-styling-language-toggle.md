# Document Styling & Language Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the generated-document styling problems (English headers over Bangla content, raw markdown artifacts, ASCII-art dividers) and add an on-demand, AI-translated, cached language toggle to the document preview page.

**Architecture:** `LabourComplaintTemplate` gets bilingual static section headings (chosen by `Case.Language`) and strips markdown/blank-line noise from AI-authored text via a new `AiTextSanitizer`, with no ASCII dividers. A new `DocumentTranslation` cache table + `IDocumentTranslationService` backs a new `POST /Document/{id}/Translate` endpoint that translates a document's full text on first request and serves cached results after. `Preview.cshtml` gets a বাংলা/English toggle wired into the site's existing `languagechange` event (`main.js`) rather than a bespoke standalone control, fetching the translation on demand and showing a fixed AI-translation disclaimer when displaying non-original-language content. PDF export is explicitly untouched — the downloaded/approved PDF stays in the document's original language only.

**Tech Stack:** ASP.NET Core MVC (.NET 8), Entity Framework Core (schema authored directly in SQL, `IEntityTypeConfiguration<T>` maps onto it — no EF migrations), xUnit + Moq for tests, vanilla JS (matches `main.js`/`chat.js` — no new client framework).

**Spec:** `docs/superpowers/specs/2026-09-10-document-styling-language-toggle-design.md`

## Global Constraints

- DB shape of `GeneratedDocument.ContentDraft`/`ContentFinal` is unchanged — still a single flattened string. No change to `IDocumentTemplate`'s interface or `DocumentGenerator`'s public signature.
- PDF export (`PdfExportService.cs`) is **not modified** in this plan — translation and styling changes apply to the web preview only. This is a deliberate, spec-approved scope boundary (legal-risk: the PDF is the lawyer-approved authoritative artifact).
- No standalone `AiTextSanitizerTests` file — sanitizer behavior is verified indirectly through `LabourComplaintTemplateTests` (explicit instruction from the human partner).
- Translation prompt must forbid adding/removing/reinterpreting legal claims and must preserve Act names, section numbers, and figures (money/dates) verbatim.
- New migration script follows the existing numbered/idempotent pattern in `scripts/` (see `scripts/10_fix_case_title_column_width.sql`) — `SET NOCOUNT ON`, `IF NOT EXISTS` guard, safe to re-run.
- Existing `IRepository<T>` generic pattern is reused for `DocumentTranslation` — no new dedicated repository interface (matches how `District`, `AiLog`, `CaseActReference`, etc. are consumed).
- This repo's `CLAUDE.md` requires editing `plans/Dependency_plan.md` the moment any covered task is touched — the last task in this plan does that.

---

### Task 1: Bilingual section headings, sanitizer, no ASCII dividers in `LabourComplaintTemplate`

**Files:**
- Create: `src/MuktoAin.Domain/Constants/LabourComplaintHeadings.cs`
- Create: `src/MuktoAin.Application/Documents/AiTextSanitizer.cs`
- Modify: `src/MuktoAin.Domain/Constants/PromptTemplates.cs`
- Modify: `src/MuktoAin.Application/Documents/Templates/LabourComplaintTemplate.cs`
- Test: `tests/MuktoAin.UnitTests/Documents/LabourComplaintTemplateTests.cs` (new file)

**Interfaces:**
- Produces: `AiTextSanitizer.Sanitize(string text) : string` — used by Task 1's template edit and available to any future template.
- Produces: `LabourComplaintHeadings.{FactsOfTheCase, ApplicableLegalProvisions, YourRights, ReliefSought, Declaration} : (string Bn, string En)` and `LabourComplaintHeadings.All : IReadOnlyList<(string Bn, string En)>`.
- No change to `IDocumentTemplate.RenderAsync`'s signature — still `Task<string> RenderAsync(Case caseEntity, RightsExplanationDto explanation)`.

- [ ] **Step 1: Write the failing sanitizer + heading tests**

Create `tests/MuktoAin.UnitTests/Documents/LabourComplaintTemplateTests.cs`:

```csharp
using MuktoAin.Application.Documents.Templates;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using Xunit;

namespace MuktoAin.UnitTests.Documents;

public class LabourComplaintTemplateTests
{
    private static Case MakeCase(string language = "bn") => new Case
    {
        CaseId = 1,
        Description = "তিন মাসের বকেয়া মজুরি পরিশোধ করা হয়নি।",
        Language = language,
        District = new District { Name = "Dhaka" }
    };

    private static RightsExplanationDto MakeExplanation(string explanation) =>
        new(explanation, Array.Empty<CitedSectionDto>(), "Disclaimer text");

    [Fact]
    public async Task RenderAsync_BanglaCase_UsesBanglaHeadings()
    {
        var template = new LabourComplaintTemplate();
        var result = await template.RenderAsync(MakeCase("bn"), MakeExplanation("আপনার অধিকার আছে।"));

        Assert.Contains("মামলার ঘটনা", result);
        Assert.Contains("প্রযোজ্য আইনি বিধান", result);
        Assert.Contains("প্রার্থিত প্রতিকার", result);
        Assert.DoesNotContain("FACTS OF THE CASE", result);
        Assert.DoesNotContain("APPLICABLE LEGAL PROVISIONS", result);
    }

    [Fact]
    public async Task RenderAsync_EnglishCase_UsesEnglishHeadings()
    {
        var template = new LabourComplaintTemplate();
        var result = await template.RenderAsync(MakeCase("en"), MakeExplanation("You have rights."));

        Assert.Contains("Facts of the Case", result);
        Assert.Contains("Applicable Legal Provisions", result);
        Assert.Contains("Relief Sought", result);
        Assert.DoesNotContain("মামলার ঘটনা", result);
    }

    [Fact]
    public async Task RenderAsync_NoAsciiDividers()
    {
        var template = new LabourComplaintTemplate();
        var result = await template.RenderAsync(MakeCase(), MakeExplanation("ব্যাখ্যা"));

        Assert.DoesNotContain('─', result);
        Assert.DoesNotContain('═', result);
    }

    [Fact]
    public async Task RenderAsync_StripsMarkdownFromAiExplanation()
    {
        var template = new LabourComplaintTemplate();
        var explanation = "**নিয়োগকর্তা মাসিক মজুরি** পরিশোধ করতে বাধ্য থাকিবেন।\n- আপনি দাবি করতে পারেন\n- আপনি ক্ষতিপূরণ চাইতে পারেন";

        var result = await template.RenderAsync(MakeCase(), MakeExplanation(explanation));

        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("\n- ", result);
    }

    [Fact]
    public async Task RenderAsync_CollapsesRepeatedBlankLinesFromAiExplanation()
    {
        var template = new LabourComplaintTemplate();
        var explanation = "প্রথম লাইন।\n\n\n\nদ্বিতীয় লাইন।";

        var result = await template.RenderAsync(MakeCase(), MakeExplanation(explanation));

        Assert.DoesNotContain("\n\n\n", result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj --filter "FullyQualifiedName~LabourComplaintTemplateTests"`
Expected: FAIL (headings are still English-only, dividers still present, no sanitizer exists yet — compile error on `District` property name is possible; if `Case`/`District` property names differ from above, adjust the test to match the real entity, not the reverse).

- [ ] **Step 3: Create the bilingual headings constants**

Create `src/MuktoAin.Domain/Constants/LabourComplaintHeadings.cs`:

```csharp
namespace MuktoAin.Domain.Constants;

/// <summary>
/// Bilingual section headings for LabourComplaintTemplate. Headers are static,
/// template-owned strings (not AI-authored) — kept here so both languages are
/// known without needing a translation call for them.
/// </summary>
public static class LabourComplaintHeadings
{
    public static readonly (string Bn, string En) FactsOfTheCase = ("মামলার ঘটনা", "Facts of the Case");
    public static readonly (string Bn, string En) ApplicableLegalProvisions = ("প্রযোজ্য আইনি বিধান", "Applicable Legal Provisions");
    public static readonly (string Bn, string En) YourRights = ("আপনার অধিকার", "Your Rights");
    public static readonly (string Bn, string En) ReliefSought = ("প্রার্থিত প্রতিকার", "Relief Sought");
    public static readonly (string Bn, string En) Declaration = ("ঘোষণা", "Declaration");

    public static IReadOnlyList<(string Bn, string En)> All { get; } = new[]
    {
        FactsOfTheCase, ApplicableLegalProvisions, YourRights, ReliefSought, Declaration
    };

    /// <summary>Picks the heading text matching the case's language ("en" = English, anything else = Bangla).</summary>
    public static string For((string Bn, string En) heading, string language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? heading.En : heading.Bn;
}
```

- [ ] **Step 4: Create the sanitizer**

Create `src/MuktoAin.Application/Documents/AiTextSanitizer.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace MuktoAin.Application.Documents;

/// <summary>
/// Strips markdown syntax and collapses excess blank lines from AI-authored
/// text before it is embedded into a plain-text legal document. Applied only
/// to AI-authored spans (e.g. Gemini's rights explanation) — never to
/// citizen-authored free text or statute text.
/// </summary>
public static class AiTextSanitizer
{
    private static readonly Regex HeadingMarker = new(@"^\s{0,3}#{1,6}\s*", RegexOptions.Compiled);
    private static readonly Regex BulletMarker = new(@"^\s{0,3}[-*]\s+", RegexOptions.Compiled);

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var cleanedLines = new List<string>(lines.Length);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Replace("**", string.Empty).Replace("__", string.Empty);
            line = HeadingMarker.Replace(line, string.Empty);
            line = BulletMarker.Replace(line, string.Empty);
            cleanedLines.Add(line.TrimEnd());
        }

        var result = new List<string>(cleanedLines.Count);
        var blankStreak = 0;
        foreach (var line in cleanedLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankStreak++;
                if (blankStreak == 1)
                    result.Add(string.Empty);
            }
            else
            {
                blankStreak = 0;
                result.Add(line);
            }
        }

        return string.Join("\n", result).Trim();
    }
}
```

- [ ] **Step 5: Rewrite `LabourComplaintTemplate` to use bilingual headings, the sanitizer, and drop ASCII dividers**

Modify `src/MuktoAin.Application/Documents/Templates/LabourComplaintTemplate.cs` — replace the full file body with:

```csharp
using System.Text;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Documents.Templates;

/// <summary>
/// Bangladesh District Labour Court complaint template.
/// Follows the format specified in Arpita_plan.md Step 2.3 —
/// structured complaint under the Bangladesh Labour Act, 2006.
/// Headings match the case's language (LabourComplaintHeadings); ASCII-art
/// dividers were removed as noise outside a monospace render, and the
/// AI-authored rights explanation is run through AiTextSanitizer before
/// being embedded (2026-09-10 styling fix).
/// </summary>
public class LabourComplaintTemplate : IDocumentTemplate
{
    public DocumentType DocumentType => DocumentType.LabourComplaint;

    public Task<string> RenderAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var language = caseEntity.Language;
        var districtName = caseEntity.District?.Name ?? "________";
        var sb = new StringBuilder();

        var isEnglish = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);

        // ── Header ──────────────────────────────────────────────
        sb.AppendLine(isEnglish ? "TO" : "বরাবর,");
        sb.AppendLine(isEnglish
            ? "The Inspector General / District Labour Court"
            : "কলকারখানা ও প্রতিষ্ঠান পরিদর্শন অধিদপ্তর / শ্রম আদালত");
        sb.AppendLine(isEnglish ? $"{districtName}, Bangladesh." : $"{districtName}, বাংলাদেশ।");
        sb.AppendLine();

        // ── Subject ─────────────────────────────────────────────
        var primarySection = explanation.CitedSections.FirstOrDefault();
        var sectionRef = primarySection != null
            ? $"Section {primarySection.SectionNumber} of"
            : string.Empty;
        sb.AppendLine($"Subject: Complaint Under {sectionRef} the Bangladesh Labour Act, 2006");
        sb.AppendLine();

        // ── Salutation ──────────────────────────────────────────
        sb.AppendLine(isEnglish ? "Respected Sir/Madam," : "মহোদয়,");
        sb.AppendLine();

        // ── Complainant Introduction ────────────────────────────
        sb.AppendLine($"I, the undersigned, resident of {districtName}, do hereby submit this complaint " +
                       "for the following violation(s) of the Bangladesh Labour Act, 2006:");
        sb.AppendLine();

        // ── Facts of the Case ───────────────────────────────────
        sb.AppendLine(LabourComplaintHeadings.For(LabourComplaintHeadings.FactsOfTheCase, language) + ":");
        sb.AppendLine(caseEntity.Description);
        sb.AppendLine();

        // ── Applicable Legal Provisions ─────────────────────────
        sb.AppendLine(LabourComplaintHeadings.For(LabourComplaintHeadings.ApplicableLegalProvisions, language) + ":");
        if (explanation.CitedSections.Count > 0)
        {
            foreach (var section in explanation.CitedSections)
            {
                sb.AppendLine($"• {section.ActTitle}, Section {section.SectionNumber}:");
                sb.AppendLine($"  {section.SectionText}");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("  [No specific sections retrieved — consult a qualified advocate]");
            sb.AppendLine();
        }

        // ── Rights Explanation (AI-authored — sanitized) ────────
        var sanitizedExplanation = AiTextSanitizer.Sanitize(explanation.Explanation);
        if (!string.IsNullOrWhiteSpace(sanitizedExplanation))
        {
            sb.AppendLine(LabourComplaintHeadings.For(LabourComplaintHeadings.YourRights, language) + ":");
            sb.AppendLine(sanitizedExplanation);
            sb.AppendLine();
        }

        // ── Relief Sought ───────────────────────────────────────
        sb.AppendLine(LabourComplaintHeadings.For(LabourComplaintHeadings.ReliefSought, language) + ":");
        sb.AppendLine("Based on the above facts and the applicable legal provisions cited herein,");
        sb.AppendLine("the complainant respectfully prays for appropriate relief including but not");
        sb.AppendLine("limited to compensation, reinstatement, and/or any other remedy the");
        sb.AppendLine("Honourable Court deems fit and proper.");
        sb.AppendLine();

        // ── Declaration ─────────────────────────────────────────
        sb.AppendLine(LabourComplaintHeadings.For(LabourComplaintHeadings.Declaration, language) + ":");
        sb.AppendLine("I hereby declare that the information provided above is true and correct to");
        sb.AppendLine("the best of my knowledge and belief. I understand that any false statement");
        sb.AppendLine("may result in legal consequences.");
        sb.AppendLine();

        // ── Signature Block ─────────────────────────────────────
        sb.AppendLine($"Date: {DateTime.UtcNow:dd MMMM, yyyy}");
        sb.AppendLine("Complainant: ________________________");
        sb.AppendLine($"District: {districtName}");
        sb.AppendLine();

        // ── Disclaimer Stamp (Surface 3 of 3) ───────────────────
        sb.AppendLine(Disclaimers.Legal);
        sb.AppendLine(Disclaimers.LegalBangla);

        return Task.FromResult(sb.ToString());
    }
}
```

Note: the Subject/salutation-intro lines keep their original English phrasing for now (they were already mixed in the pre-existing template and are outside this plan's section-heading fix) — only the five section headings and the letter's opening/closing boilerplate that this plan explicitly targets are language-matched. If you want those fully bilingual too, that's a natural follow-up, not part of this plan.

- [ ] **Step 6: Add the "no markdown" rule to the AI prompts**

Modify `src/MuktoAin.Domain/Constants/PromptTemplates.cs`:

```csharp
namespace MuktoAin.Domain.Constants;

public static class PromptTemplates
{
    public const string RightsExplanation = """
        You are a legal information assistant for Bangladesh.
        A citizen has described this problem: {problem}

        Based ONLY on the following statutory sections, explain their rights
        in plain {language}. Cite specific Act names and Section numbers.

        Relevant statutory text:
        {context}

        Rules:
        - Only cite sections provided above. Never fabricate citations.
        - Use simple language a non-lawyer can understand.
        - If the provided sections don't cover the problem, say so explicitly.
        - Return plain prose only — do not use markdown syntax (no **, #, -, backticks).
        - End with: {disclaimer}
        """;

    public const string DocumentDrafting = """
        You are a legal document drafting assistant for Bangladesh.
        A citizen has described this problem: {problem}
        The document type requested is: {documentType}

        Draft the document using ONLY the following statutory sections as the legal basis.
        Cite specific Act names and Section numbers where applicable.

        Relevant statutory text:
        {context}

        Rules:
        - Only cite sections provided above. Never fabricate citations.
        - Use formal Bangladeshi legal-document structure and plain {language}.
        - Leave clearly marked placeholders like [YOUR NAME] for citizen-specific details.
        - If the provided sections don't cover the problem, say so explicitly.
        - Return plain prose only — do not use markdown syntax (no **, #, -, backticks).
        - End with: {disclaimer}
        """;

    public const string Translation = """
        You are a precise legal-document translator.
        Translate the following legal document text from {sourceLanguage} to {targetLanguage}.

        Rules:
        - Produce a literal, faithful translation only. Do not add, remove, or reinterpret any legal claim.
        - Preserve Act names, Section numbers, monetary figures, and dates exactly as written — do not convert, round, or reformat them.
        - Preserve the original paragraph and section structure.
        - Do not use markdown syntax (no **, #, -, backticks).
        - Output ONLY the translated text, with no preamble or explanation.

        Text to translate:
        {content}
        """;
}
```

- [ ] **Step 7: Run the tests and verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj --filter "FullyQualifiedName~LabourComplaintTemplateTests"`
Expected: PASS (5/5)

- [ ] **Step 8: Run the full unit test suite to check nothing else broke**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj`
Expected: PASS. `DocumentGeneratorTests` still passes unchanged — `IDocumentTemplate.RenderAsync`'s signature didn't change, only `LabourComplaintTemplate`'s internal output.

- [ ] **Step 9: Commit**

```bash
git add src/MuktoAin.Domain/Constants/LabourComplaintHeadings.cs src/MuktoAin.Application/Documents/AiTextSanitizer.cs src/MuktoAin.Domain/Constants/PromptTemplates.cs src/MuktoAin.Application/Documents/Templates/LabourComplaintTemplate.cs tests/MuktoAin.UnitTests/Documents/LabourComplaintTemplateTests.cs
git commit -m "fix(documents): match section headings to case language, sanitize AI text, drop ASCII dividers"
```

---

### Task 2: `DocumentTranslation` cache entity, EF config, and migration

**Files:**
- Create: `src/MuktoAin.Domain/Entities/DocumentTranslation.cs`
- Create: `src/MuktoAin.Infrastructure/Data/Configurations/DocumentTranslationConfiguration.cs`
- Create: `scripts/11_document_translation.sql`
- Modify: `src/MuktoAin.Infrastructure/Data/AppDbContext.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `DocumentTranslation { DocumentTranslationId, DocumentId, Language, TranslatedContent, CreatedAt }`, consumed via `IRepository<DocumentTranslation>` (generic — already DI-registered) by Task 4's `DocumentTranslationService`.

- [ ] **Step 1: Create the entity**

Create `src/MuktoAin.Domain/Entities/DocumentTranslation.cs`:

```csharp
namespace MuktoAin.Domain.Entities;

/// <summary>
/// Cache of on-demand AI translations of a GeneratedDocument's content into
/// a target language. One row per (DocumentId, Language). Never the
/// authoritative document — see DocumentTranslationResult.IsTranslated /
/// the fixed disclaimer surfaced alongside translated content.
/// </summary>
public class DocumentTranslation
{
    public int DocumentTranslationId { get; set; }

    public int DocumentId { get; set; }
    public GeneratedDocument Document { get; set; } = null!;

    // "bn" or "en" — the language TranslatedContent is written in.
    public string Language { get; set; } = string.Empty;

    public string TranslatedContent { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

Create `src/MuktoAin.Infrastructure/Data/Configurations/DocumentTranslationConfiguration.cs`:

```csharp
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
```

- [ ] **Step 3: Add the DbSet to AppDbContext**

Modify `src/MuktoAin.Infrastructure/Data/AppDbContext.cs` — find the line `public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();` and add immediately after it:

```csharp
    public DbSet<DocumentTranslation> DocumentTranslations => Set<DocumentTranslation>();
```

(No change needed to `OnModelCreating` — `ApplyConfigurationsFromAssembly` already picks up the new `IEntityTypeConfiguration<DocumentTranslation>` automatically.)

- [ ] **Step 4: Write the migration script**

Create `scripts/11_document_translation.sql`:

```sql
/* ============================================================
   MuktoAin — Add DOCUMENT_TRANSLATION table (2026-09-10)

   Backs the on-demand AI-translated language toggle on document
   preview. One cached row per (DocumentId, Language) — see
   docs/superpowers/specs/2026-09-10-document-styling-language-toggle-design.md.
   Never the authoritative document; PDF export and ContentDraft/
   ContentFinal are untouched by this table.

   IDEMPOTENT: safe to re-run. Execute in SSMS against the MuktoAin
   database.
   ============================================================ */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DOCUMENT_TRANSLATION' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[DOCUMENT_TRANSLATION]
    (
        DocumentTranslationId INT IDENTITY(1,1) NOT NULL,
        DocumentId            INT               NOT NULL,
        Language              VARCHAR(2)        NOT NULL,
        TranslatedContent     NVARCHAR(MAX)     NOT NULL,
        CreatedAt             DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_DOCUMENT_TRANSLATION PRIMARY KEY (DocumentTranslationId),
        CONSTRAINT FK_DOCUMENT_TRANSLATION_GeneratedDocument FOREIGN KEY (DocumentId)
            REFERENCES [dbo].[GENERATED_DOCUMENT] (DocumentId)
    );

    CREATE UNIQUE INDEX UX_DOCUMENT_TRANSLATION_DocumentId_Language
        ON [dbo].[DOCUMENT_TRANSLATION] (DocumentId, Language);
END
GO
```

- [ ] **Step 5: Build to verify the entity/config compile and map cleanly**

Run: `dotnet build src/MuktoAin.sln`
Expected: Build succeeds, no EF model-mapping errors.

- [ ] **Step 6: Apply the migration**

Run the new script against the local MuktoAin database the same way prior scripts in `scripts/` are applied for this project (SSMS or the existing `scripts/run-all.ps1` convention) — this plan does not change that process, only adds one more idempotent script to it.

- [ ] **Step 7: Commit**

```bash
git add src/MuktoAin.Domain/Entities/DocumentTranslation.cs src/MuktoAin.Infrastructure/Data/Configurations/DocumentTranslationConfiguration.cs src/MuktoAin.Infrastructure/Data/AppDbContext.cs scripts/11_document_translation.sql
git commit -m "feat(documents): add DocumentTranslation cache table"
```

---

### Task 3: `IDocumentTranslationService` + `DocumentTranslationResult` + translation disclaimer

**Files:**
- Create: `src/MuktoAin.Domain/Models/DocumentTranslationResult.cs`
- Create: `src/MuktoAin.Domain/Interfaces/Services/IDocumentTranslationService.cs`
- Modify: `src/MuktoAin.Domain/Constants/Disclaimers.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `DocumentTranslationResult(string Content, bool IsTranslated, string Disclaimer)`; `IDocumentTranslationService.GetOrTranslateAsync(int documentId, string content, string sourceLanguage, string targetLanguage, CancellationToken ct = default) : Task<DocumentTranslationResult>` — implemented in Task 4, consumed by Task 5's controller.
- Produces: `Disclaimers.TranslationDisclaimer(string originalLanguage) : string`.

- [ ] **Step 1: Add the translation disclaimer**

Modify `src/MuktoAin.Domain/Constants/Disclaimers.cs` — add this method inside the existing `Disclaimers` class, after `ForLanguage`:

```csharp
    /// <summary>
    /// Disclaimer shown alongside an on-demand AI translation of a document,
    /// naming the original language as the authoritative version.
    /// </summary>
    public static string TranslationDisclaimer(string originalLanguage) =>
        string.Equals(originalLanguage, "en", StringComparison.OrdinalIgnoreCase)
            ? "AI-translated for convenience — the English version is the authoritative document."
            : "সুবিধার জন্য এআই দ্বারা অনূদিত — বাংলা সংস্করণটি প্রামাণ্য দলিল।";
```

- [ ] **Step 2: Create the result model**

Create `src/MuktoAin.Domain/Models/DocumentTranslationResult.cs`:

```csharp
namespace MuktoAin.Domain.Models;

/// <summary>Result of a document translation request (cache hit or fresh AI translation).</summary>
public record DocumentTranslationResult(string Content, bool IsTranslated, string Disclaimer);
```

- [ ] **Step 3: Create the service interface**

Create `src/MuktoAin.Domain/Interfaces/Services/IDocumentTranslationService.cs`:

```csharp
using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Services;

/// <summary>
/// On-demand, cached translation of a generated document's full text.
/// Implemented in Application via the existing Gemini IAiService path.
/// </summary>
public interface IDocumentTranslationService
{
    Task<DocumentTranslationResult> GetOrTranslateAsync(
        int documentId,
        string content,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/MuktoAin.sln`
Expected: Build succeeds (nothing implements the interface yet — that's fine, it's just declared).

- [ ] **Step 5: Commit**

```bash
git add src/MuktoAin.Domain/Models/DocumentTranslationResult.cs src/MuktoAin.Domain/Interfaces/Services/IDocumentTranslationService.cs src/MuktoAin.Domain/Constants/Disclaimers.cs
git commit -m "feat(documents): add translation service contract and disclaimer"
```

---

### Task 4: `DocumentTranslationService` implementation + tests

**Files:**
- Create: `src/MuktoAin.Application/Services/DocumentTranslationService.cs`
- Test: `tests/MuktoAin.UnitTests/Services/DocumentTranslationServiceTests.cs`

**Interfaces:**
- Consumes: `IRepository<DocumentTranslation>` (Task 2), `MuktoAin.Domain.Interfaces.IAiService.GenerateContentAsync(string prompt, CancellationToken ct = default) : Task<string>` (existing), `PromptTemplates.Translation` (Task 1), `Disclaimers.TranslationDisclaimer` (Task 3).
- Produces: `DocumentTranslationService : IDocumentTranslationService` — registered in Task 5, consumed by Task 6's controller.

- [ ] **Step 1: Write the failing tests**

Create `tests/MuktoAin.UnitTests/Services/DocumentTranslationServiceTests.cs`:

```csharp
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class DocumentTranslationServiceTests
{
    private readonly Mock<IRepository<DocumentTranslation>> _translationRepo = new();
    private readonly Mock<MuktoAin.Domain.Interfaces.IAiService> _aiService = new();

    private DocumentTranslationService MakeService() =>
        new(_translationRepo.Object, _aiService.Object);

    [Fact]
    public async Task GetOrTranslateAsync_CacheHit_ReturnsCachedContent_NoAiCall()
    {
        var cached = new DocumentTranslation
        {
            DocumentTranslationId = 1,
            DocumentId = 42,
            Language = "en",
            TranslatedContent = "Cached English text",
            CreatedAt = DateTime.UtcNow
        };
        _translationRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { cached });

        var service = MakeService();
        var result = await service.GetOrTranslateAsync(42, "বাংলা লেখা", "bn", "en");

        Assert.Equal("Cached English text", result.Content);
        Assert.True(result.IsTranslated);
        _aiService.Verify(a => a.GenerateContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _translationRepo.Verify(r => r.AddAsync(It.IsAny<DocumentTranslation>()), Times.Never);
    }

    [Fact]
    public async Task GetOrTranslateAsync_CacheMiss_CallsAiAndPersists()
    {
        _translationRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<DocumentTranslation>());
        _aiService.Setup(a => a.GenerateContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Freshly translated English text of reasonable length");

        var service = MakeService();
        var result = await service.GetOrTranslateAsync(42, "মূল বাংলা লেখা যথেষ্ট দীর্ঘ", "bn", "en");

        Assert.Equal("Freshly translated English text of reasonable length", result.Content);
        Assert.True(result.IsTranslated);
        _translationRepo.Verify(r => r.AddAsync(It.Is<DocumentTranslation>(
            t => t.DocumentId == 42 && t.Language == "en" && t.TranslatedContent == "Freshly translated English text of reasonable length")), Times.Once);
        _translationRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetOrTranslateAsync_EmptyAiResult_ThrowsAndDoesNotCache()
    {
        _translationRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<DocumentTranslation>());
        _aiService.Setup(a => a.GenerateContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var service = MakeService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetOrTranslateAsync(42, "একটি দীর্ঘ মূল বাংলা লেখা এখানে আছে", "bn", "en"));
        _translationRepo.Verify(r => r.AddAsync(It.IsAny<DocumentTranslation>()), Times.Never);
    }

    [Fact]
    public async Task GetOrTranslateAsync_PromptIncludesSourceAndTargetLanguageAndContent()
    {
        _translationRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<DocumentTranslation>());
        string? capturedPrompt = null;
        _aiService.Setup(a => a.GenerateContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => capturedPrompt = p)
            .ReturnsAsync("Translated output long enough to pass the quality check");

        var service = MakeService();
        await service.GetOrTranslateAsync(42, "মূল বিষয়বস্তু এখানে যথেষ্ট দীর্ঘ", "bn", "en");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("bn", capturedPrompt);
        Assert.Contains("en", capturedPrompt);
        Assert.Contains("মূল বিষয়বস্তু এখানে যথেষ্ট দীর্ঘ", capturedPrompt);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj --filter "FullyQualifiedName~DocumentTranslationServiceTests"`
Expected: FAIL (compile error — `DocumentTranslationService` doesn't exist yet).

- [ ] **Step 3: Implement the service**

Create `src/MuktoAin.Application/Services/DocumentTranslationService.cs`:

```csharp
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.Application.Services;

/// <summary>
/// On-demand, cached translation of a generated document's full text.
/// Checks DocumentTranslation for an existing (DocumentId, Language) row
/// before calling Gemini; persists successful translations so repeat
/// toggles never re-call the AI. A too-short/empty AI result is treated
/// as a failure and is not cached (see spec's error handling section).
/// </summary>
public class DocumentTranslationService : IDocumentTranslationService
{
    private readonly IRepository<DocumentTranslation> _translationRepo;
    private readonly MuktoAin.Domain.Interfaces.IAiService _aiService;

    public DocumentTranslationService(
        IRepository<DocumentTranslation> translationRepo,
        MuktoAin.Domain.Interfaces.IAiService aiService)
    {
        _translationRepo = translationRepo;
        _aiService = aiService;
    }

    public async Task<DocumentTranslationResult> GetOrTranslateAsync(
        int documentId,
        string content,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default)
    {
        var disclaimer = Disclaimers.TranslationDisclaimer(sourceLanguage);

        var existing = await _translationRepo.GetAllAsync();
        var cached = existing.FirstOrDefault(t =>
            t.DocumentId == documentId &&
            string.Equals(t.Language, targetLanguage, StringComparison.OrdinalIgnoreCase));

        if (cached != null)
            return new DocumentTranslationResult(cached.TranslatedContent, true, disclaimer);

        var prompt = PromptTemplates.Translation
            .Replace("{sourceLanguage}", sourceLanguage)
            .Replace("{targetLanguage}", targetLanguage)
            .Replace("{content}", content);

        var translated = await _aiService.GenerateContentAsync(prompt, ct);

        // Cheap quality heuristic: empty, or drastically shorter than the
        // source, is treated as a failed translation rather than cached.
        if (string.IsNullOrWhiteSpace(translated) || translated.Length < content.Length / 3)
            throw new InvalidOperationException("Translation result failed the quality check.");

        var record = new DocumentTranslation
        {
            DocumentId = documentId,
            Language = targetLanguage,
            TranslatedContent = translated,
            CreatedAt = DateTime.UtcNow
        };
        await _translationRepo.AddAsync(record);
        await _translationRepo.SaveChangesAsync();

        return new DocumentTranslationResult(translated, true, disclaimer);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj --filter "FullyQualifiedName~DocumentTranslationServiceTests"`
Expected: PASS (4/4)

- [ ] **Step 5: Commit**

```bash
git add src/MuktoAin.Application/Services/DocumentTranslationService.cs tests/MuktoAin.UnitTests/Services/DocumentTranslationServiceTests.cs
git commit -m "feat(documents): implement cached on-demand document translation"
```

---

### Task 5: Register the translation service in DI

**Files:**
- Modify: `src/MuktoAin.Web/Program.cs`

**Interfaces:**
- Consumes: `DocumentTranslationService` (Task 4), `IDocumentTranslationService` (Task 3).
- Produces: `IDocumentTranslationService` resolvable from the DI container — consumed by Task 6's `DocumentController`.

- [ ] **Step 1: Add the registration**

Modify `src/MuktoAin.Web/Program.cs` — find the block:

```csharp
// A-2.4: Document lifecycle service
builder.Services.AddScoped<DocumentService>();
```

and add immediately after it:

```csharp
// 2026-09-10: on-demand, cached document translation for the preview language toggle
builder.Services.AddScoped<IDocumentTranslationService, DocumentTranslationService>();
```

Add the two `using` statements at the top of `Program.cs` if not already present (check the existing `using` block first — `MuktoAin.Application.Services` and `MuktoAin.Domain.Interfaces.Services` are almost certainly already imported since `AiOrchestrationService`/`IAiOrchestrationService` are registered a few lines above; only add what's missing).

- [ ] **Step 2: Build to verify DI wiring compiles and resolves**

Run: `dotnet build src/MuktoAin.sln`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/MuktoAin.Web/Program.cs
git commit -m "chore(di): register IDocumentTranslationService"
```

---

### Task 6: `DocumentController.Translate` endpoint + tests

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/DocumentController.cs`
- Test: `tests/MuktoAin.UnitTests/Controllers/DocumentControllerTests.cs`

**Interfaces:**
- Consumes: `IDocumentTranslationService.GetOrTranslateAsync` (Task 4), `IRepository<GeneratedDocument>` (existing).
- Produces: `POST /Document/{id}/Translate?lang={bn|en}` → JSON `{ content, isTranslated, disclaimer }` on success, `{ error }` with a 4xx/5xx status on failure — consumed by Task 7's `document-preview.js`.

- [ ] **Step 1: Write the failing controller tests**

Modify `tests/MuktoAin.UnitTests/Controllers/DocumentControllerTests.cs` — add `using Moq;` and `using MuktoAin.Domain.Interfaces.Services;` and `using MuktoAin.Domain.Models;` to the top if not already present, add an `_translationService` mock field wired into the constructor call, and append these test methods to the `DocumentControllerTests` class:

```csharp
    [Fact]
    public async Task Translate_InvalidId_ReturnsBadRequest()
    {
        var result = await _controller.Translate(0, "en");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Translate_InvalidLanguage_ReturnsBadRequest()
    {
        var result = await _controller.Translate(10, "fr");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Translate_DocumentNotFound_ReturnsNotFound()
    {
        _docRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((GeneratedDocument?)null);

        var result = await _controller.Translate(99, "en");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Translate_SameAsSourceLanguage_ReturnsOriginalWithoutCallingTranslationService()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 10,
            CaseId = 42,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = "বাংলা লেখা এখানে আছে",
            Status = DocumentStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        _docRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(doc);

        var result = await _controller.Translate(10, "bn");

        var ok = Assert.IsType<OkObjectResult>(result);
        _translationService.Verify(
            t => t.GetOrTranslateAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Translate_DifferentLanguage_CallsTranslationServiceAndReturnsOk()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 10,
            CaseId = 42,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = "বাংলা লেখা এখানে আছে",
            Status = DocumentStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        _docRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(doc);
        _translationService
            .Setup(t => t.GetOrTranslateAsync(10, doc.ContentDraft, "bn", "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTranslationResult("English text", true, "AI-translated disclaimer"));

        var result = await _controller.Translate(10, "en");

        Assert.IsType<OkObjectResult>(result);
        _translationService.Verify(
            t => t.GetOrTranslateAsync(10, doc.ContentDraft, "bn", "en", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Translate_TranslationServiceThrows_ReturnsErrorStatus()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 10,
            CaseId = 42,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = "বাংলা লেখা এখানে আছে",
            Status = DocumentStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        _docRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(doc);
        _translationService
            .Setup(t => t.GetOrTranslateAsync(10, doc.ContentDraft, "bn", "en", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Translation result failed the quality check."));

        var result = await _controller.Translate(10, "en");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, status.StatusCode);
    }
```

Also update the constructor in the test class's setup to pass a translation service mock:

```csharp
    private readonly Mock<IRepository<GeneratedDocument>> _docRepo;
    private readonly Mock<IDocumentTranslationService> _translationService;
    private readonly DocumentController _controller;

    public DocumentControllerTests()
    {
        _docRepo = new Mock<IRepository<GeneratedDocument>>();
        _translationService = new Mock<IDocumentTranslationService>();
        var httpContext = new DefaultHttpContext();
        _controller = new DocumentController(
            Mock.Of<ILogger<DocumentController>>(),
            _docRepo.Object,
            documentService: null,
            translationService: _translationService.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj --filter "FullyQualifiedName~DocumentControllerTests"`
Expected: FAIL (compile error — `Translate` action and the new constructor parameter don't exist yet).

- [ ] **Step 3: Add the `Translate` action and constructor parameter**

Modify `src/MuktoAin.Web/Controllers/DocumentController.cs`:

Add these `using` statements at the top:

```csharp
using System.Text.RegularExpressions;
using MuktoAin.Domain.Interfaces.Services;
```

Change the constructor from:

```csharp
    public DocumentController(
        ILogger<DocumentController> logger,
        IRepository<GeneratedDocument>? docRepo = null,
        DocumentService? documentService = null)
    {
        _logger = logger;
        _docRepo = docRepo;
        _documentService = documentService;
    }
```

to:

```csharp
    private readonly IDocumentTranslationService? _translationService;

    public DocumentController(
        ILogger<DocumentController> logger,
        IRepository<GeneratedDocument>? docRepo = null,
        DocumentService? documentService = null,
        IDocumentTranslationService? translationService = null)
    {
        _logger = logger;
        _docRepo = docRepo;
        _documentService = documentService;
        _translationService = translationService;
    }
```

(Move the `private readonly IDocumentTranslationService? _translationService;` field declaration up next to the other private fields at the top of the class instead of inline before the constructor, matching the existing field-declaration style.)

Add the `Translate` action (placed after `Preview`, before `Download`):

```csharp
    [HttpPost]
    public async Task<IActionResult> Translate(int id, string lang)
    {
        if (id <= 0)
            return BadRequest(new { error = "Invalid document id." });

        if (!string.Equals(lang, "bn", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Unsupported language." });

        if (_docRepo == null || _translationService == null)
            return StatusCode(503, new { error = "Translation service unavailable." });

        GeneratedDocument? doc;
        try
        {
            doc = await _docRepo.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load document {DocumentId} for translation", id);
            return StatusCode(503, new { error = "Translation service unavailable." });
        }

        if (doc == null)
            return NotFound();

        var content = doc.ContentFinal ?? doc.ContentDraft;
        var sourceLanguage = Regex.IsMatch(content, @"\p{IsBengali}") ? "bn" : "en";
        var targetLanguage = lang.ToLowerInvariant();

        if (string.Equals(sourceLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase))
            return Ok(new { content, isTranslated = false, disclaimer = (string?)null });

        try
        {
            var result = await _translationService.GetOrTranslateAsync(doc.DocumentId, content, sourceLanguage, targetLanguage);
            return Ok(new { content = result.Content, isTranslated = result.IsTranslated, disclaimer = result.Disclaimer });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed for document {DocumentId} to {Language}", id, targetLanguage);
            return StatusCode(502, new { error = "Translation unavailable right now — showing the original." });
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj --filter "FullyQualifiedName~DocumentControllerTests"`
Expected: PASS (all tests, old and new).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/MuktoAin.Web/Controllers/DocumentController.cs tests/MuktoAin.UnitTests/Controllers/DocumentControllerTests.cs
git commit -m "feat(documents): add POST /Document/{id}/Translate endpoint"
```

---

### Task 7: Wire the language toggle into `Preview.cshtml`

**Files:**
- Create: `src/MuktoAin.Web/wwwroot/assets/js/document-preview.js`
- Modify: `src/MuktoAin.Web/Views/Document/Preview.cshtml`

**Interfaces:**
- Consumes: `POST /Document/{id}/Translate?lang=…` (Task 6); the site's existing `window` `languagechange` `CustomEvent` (`detail.lang`), dispatched by `src/MuktoAin.Web/wwwroot/assets/js/main.js:1482`.
- Produces: nothing further downstream — this is the leaf UI task.

- [ ] **Step 1: Add the document-id data attribute and disclaimer badge markup**

Modify `src/MuktoAin.Web/Views/Document/Preview.cshtml` — change the document body line:

```cshtml
        <div class="document-preview" style="font-size: 15px; line-height: 1.8; color: var(--ink); white-space: pre-wrap; word-break: break-word;">@displayContent</div>
```

to:

```cshtml
        <div id="documentTranslationNotice" class="alert alert-warning card-pad mb-3" style="display:none; background: rgba(234, 179, 8, 0.1); border: 1px solid rgba(234, 179, 8, 0.3); border-radius: 8px; font-size: 13px;" role="status"></div>
        <div id="documentPreviewBody" class="document-preview" data-document-id="@Model.DocumentId" data-original-content="@Model.ContentDraft" style="font-size: 15px; line-height: 1.8; color: var(--ink); white-space: pre-wrap; word-break: break-word;">@displayContent</div>
```

(`data-original-content` holds the untouched original so switching back to it never needs a network call — Razor HTML-encodes attribute values automatically, so no extra escaping is needed here.)

Add the script reference at the bottom of the file, after the closing `</div>` of the page's root container:

```cshtml
<script src="~/assets/js/document-preview.js"></script>
```

- [ ] **Step 2: Create the toggle script**

Create `src/MuktoAin.Web/wwwroot/assets/js/document-preview.js`:

```javascript
(function () {
  "use strict";

  var bodyEl = document.getElementById("documentPreviewBody");
  if (!bodyEl) return; // Not on a document preview page.

  var noticeEl = document.getElementById("documentTranslationNotice");
  var documentId = bodyEl.getAttribute("data-document-id");
  var originalContent = bodyEl.getAttribute("data-original-content") || bodyEl.textContent;
  var cache = {}; // lang -> { content, isTranslated, disclaimer }
  var inFlight = false;

  function showOriginal() {
    bodyEl.textContent = originalContent;
    if (noticeEl) noticeEl.style.display = "none";
  }

  function showTranslated(result) {
    bodyEl.textContent = result.content;
    if (noticeEl) {
      if (result.isTranslated && result.disclaimer) {
        noticeEl.textContent = result.disclaimer;
        noticeEl.style.display = "block";
      } else {
        noticeEl.style.display = "none";
      }
    }
  }

  function showError() {
    bodyEl.textContent = originalContent;
    if (noticeEl) {
      noticeEl.textContent = "Translation unavailable right now — showing the original. / অনুবাদ সাময়িকভাবে অনুপলব্ধ — মূল লেখা দেখানো হচ্ছে।";
      noticeEl.style.display = "block";
    }
  }

  function applyDocumentLanguage(lang) {
    if (!documentId || inFlight) return;

    if (cache[lang]) {
      if (cache[lang].isOriginal) showOriginal();
      else showTranslated(cache[lang]);
      return;
    }

    inFlight = true;
    fetch("/Document/" + encodeURIComponent(documentId) + "/Translate?lang=" + encodeURIComponent(lang), {
      method: "POST"
    })
      .then(function (res) {
        if (!res.ok) throw new Error("Translation request failed");
        return res.json();
      })
      .then(function (data) {
        if (data.isTranslated === false) {
          cache[lang] = { isOriginal: true };
          showOriginal();
        } else {
          cache[lang] = data;
          showTranslated(data);
        }
      })
      .catch(function () {
        showError();
      })
      .finally(function () {
        inFlight = false;
      });
  }

  window.addEventListener("languagechange", function (ev) {
    var lang = ev && ev.detail && ev.detail.lang;
    if (lang === "bn" || lang === "en") applyDocumentLanguage(lang);
  });
})();
```

- [ ] **Step 3: Manual verification**

Run the app locally (however this repo normally runs it — `dotnet run --project src/MuktoAin.Web`), open a document at `/Document/Preview/{id}` for an existing generated document, and use the site's existing বাংলা/English toggle in the nav:
- Confirm switching to the document's original language shows it instantly with no network request (check browser dev tools Network tab).
- Confirm switching to the other language shows a brief loading state then the translated text plus the disclaimer banner.
- Confirm switching back and forth a second time uses the cached response (no repeat network request).
- Confirm print (`window.print()`, unaffected by this task) and PDF download (if approved) still show the original-language content only.

- [ ] **Step 4: Commit**

```bash
git add src/MuktoAin.Web/Views/Document/Preview.cshtml src/MuktoAin.Web/wwwroot/assets/js/document-preview.js
git commit -m "feat(documents): wire on-demand translation into the site language toggle"
```

---

### Task 8: Update `plans/Dependency_plan.md`

**Files:**
- Modify: `plans/Dependency_plan.md`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing (documentation-only task, required by this repo's `CLAUDE.md`).

- [ ] **Step 1: Append a note to the A-2.2 and A-2.3 lines**

Modify `plans/Dependency_plan.md` — find the `[A-2.2]` line (ends `...verified with unit tests~~`) and the `[A-2.3]` line (ends `...verified with unit tests~~`), and append to each, immediately before the closing `~~`, a short dated note in the same style as this file's other post-completion notes (e.g. the `A-2.5` line's trailing note):

For `[A-2.2]`, append: ` 2026-09-10 styling fix: no functional change to DocumentGenerator/IDocumentTemplate.`

For `[A-2.3]`, append: ` 2026-09-10 styling fix: LabourComplaintTemplate headings now match Case.Language (LabourComplaintHeadings), AI-authored rights explanation sanitized (AiTextSanitizer strips markdown/collapses blank lines), ASCII-art dividers removed; also added the on-demand cached translation toggle (DocumentTranslation table, IDocumentTranslationService, POST /Document/{id}/Translate) wired into Preview.cshtml's existing language switcher. PDF export intentionally untouched — see docs/superpowers/specs/2026-09-10-document-styling-language-toggle-design.md.`

- [ ] **Step 2: Commit**

```bash
git add plans/Dependency_plan.md
git commit -m "docs: record document styling/translation fix in Dependency_plan.md"
```

---

## Self-Review Notes

- **Spec coverage:** bilingual headings → Task 1; sanitizer → Task 1; ASCII dividers removed → Task 1; translation cache table → Task 2; translation service/prompt/disclaimer → Tasks 1 & 3; translation endpoint → Task 6; view wiring → Task 7; PDF explicitly untouched → honored (no PDF task exists); mandatory plan tracking → Task 8.
- **Simplification disclosed:** the brainstorm mockup showed each document section as its own styled card with a CSS rule under the heading. Implementing that would require either changing `IDocumentTemplate`'s return type to a structured section list (touching `DocumentGenerator`, DB-shape assumptions, and every future template) or fragile re-parsing of the flat persisted string in the view. Given the spec's own constraint that DB shape stays unchanged, this plan achieves the actual complaint (language-matched headings, no markdown artifacts, no ASCII-art noise) by cleaning the flattened text at the source instead, without introducing that structural risk. The view keeps today's single preformatted block, now rendering clean content. This is a scope trim from the mockup's visual polish, not from the spec's functional requirements.
- **No standalone sanitizer test file**, per instruction — covered by `LabourComplaintTemplateTests`.
- **Type consistency check:** `IDocumentTranslationService.GetOrTranslateAsync` signature matches between Task 3's declaration, Task 4's implementation and tests, and Task 6's controller call. `DocumentTranslationResult(Content, IsTranslated, Disclaimer)` field names match across Tasks 3, 4, 6.
