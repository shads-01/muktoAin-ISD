# QA Benchmark Chain (Zero-Shot & Few-Shot IRAC) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the S-3.1 → S-3.2 → S-3.3 QA benchmark chain: a loader for the 2,165-question Bangladesh Legal QA dataset, a zero-shot baseline evaluation runner that scores precision/recall/F1 of statutory citations against gold references, and a few-shot IRAC prompt variant re-run with a zero-shot vs few-shot comparison report.

**Architecture:** Three Application-layer pieces behind new interfaces: `BenchmarkLoaderService` (`IBenchmarkLoader`, S-3.1) parses the dataset into canonical DTOs; `BenchmarkScorer` (Task 3, shared) does reference parsing + matching + per-question metrics; `BenchmarkRunnerService` (`IBenchmarkRunner`, S-3.2/S-3.3) mirrors the existing `AiOrchestrationService` pipeline steps (retrieve → assemble prompt → generate → inject disclaimer) through the same DI seams, scores every question, aggregates per-Act category results, and writes JSON reports to `data/benchmark/results/`. The runner deliberately bypasses `IAiLogService`/`IRepository` persistence (no `CaseId`, no `AI_LOG` rows, no quota-budget side effects) while using the identical `IRagContextBuilder`/`IPromptAssembler`/`IAiService`/`DisclaimerInjector` seams. Few-shot IRAC (S-3.3) extends `PromptTemplates` + `PromptAssembler` with a second assembly method so the existing zero-shot path is untouched.

**Tech Stack:** .NET 8 / C#, `System.Text.Json` (no new JSON libraries), xUnit 2.9.3 + Moq 4.20.72, `Microsoft.AspNetCore.Mvc.Testing` 8.0.11 (only new package — enables the opt-in full-pipeline integration run via `WebApplicationFactory<Program>`).

**Spec:** `.agent/spec/requirements.md` §4 (NLP / Evaluation Workstream: 2,165-pair dataset, retrieval top-k recall + answer correctness, few-shot IRAC steering via `PromptAssembler.cs`, baseline-vs-few-shot reporting). Also `plans/Arpita_plan.md` §"S-3.1 / S-3.2 / S-3.3" and `docs/attribution-CC-BY-SA-4.0.md` §2 (dataset license CC BY 4.0).

## Global Constraints

- **NEVER commit, stage, push, or amend** — AGENTS.md §6: Shads is the sole committer. Every former "Commit" step below is a "Record completion in plans/Dependency_plan.md" step instead.
- No new database tables/scripts — the benchmark harness performs **zero DB writes**; it reads the vector corpus via `IRagContextBuilder` exactly like production retrieval.
- Disclaimer policy (`.agent/spec/requirements.md` §3): every response captured in results JSON has already passed through `DisclaimerInjector` (surface 2 of 3) — the runner must not strip or re-add it.
- Retrieval is **vector-primary via `IRagContextBuilder` only** — no concurrent FTS hybrid queries (AGENTS.md §3 rule 3); FTS fallback happens inside `RagContextBuilder` as today.
- .NET 8, `Nullable` enabled, `ImplicitUsings` enabled; xUnit 2.9.3 + Moq 4.20.72 (exact versions in `tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj`).
- Clean Architecture placement: raw-row parsing DTOs + loader + scorer + runner in `MuktoAin.Application`; the `BenchmarkPromptVariant` enum in `MuktoAin.Domain/Enums` (repo convention); few-shot template constants in `MuktoAin.Domain/Constants/PromptTemplates.cs` (next to the existing templates).
- The full dataset file is **git-ignored** (`data/bangladesh-legal-qa-dataset.json`, see `data/README.md` §2.2) — never stage it for commit. The committed fallback is `data/benchmark/benchmark-sample.json` (5 rows; CC BY 4.0 attribution already covered by `docs/attribution-CC-BY-SA-4.0.md` §2).
- Live full-corpus runs consume real Gemini free-tier quota — all credential-requiring integration tests are **opt-in** via `MUKTOAIN_RUN_QA_BENCHMARK=1` and must be a silent no-op otherwise.

---

### Task 1: Canonical Dataset Schema, DTOs, and Committed Seed Sample

**Files:**
- Create: `src/MuktoAin.Domain/Enums/BenchmarkPromptVariant.cs`
- Create: `src/MuktoAin.Application/DTOs/BenchmarkQuestionDto.cs`
- Create: `src/MuktoAin.Application/DTOs/BenchmarkDatasetRow.cs`
- Create: `src/MuktoAin.Application/DTOs/BenchmarkDataset.cs`
- Create: `src/MuktoAin.Application/DTOs/BenchmarkRunOptions.cs`
- Create: `src/MuktoAin.Application/DTOs/BenchmarkQuestionScoreDto.cs`
- Create: `src/MuktoAin.Application/DTOs/BenchmarkCategoryResultDto.cs`
- Create: `src/MuktoAin.Application/DTOs/BenchmarkRunResultDto.cs`
- Create: `data/benchmark/benchmark-sample.json`
- Modify: `data/README.md` (§2.2: add schema reference + sample-file note)

**Interfaces:**
- Produces: `BenchmarkPromptVariant` enum (`ZeroShot = 0`; `FewShotIrac = 1` added in Task 6), `BenchmarkQuestionDto` (record: `DatasetId, Question, Language, Category, QuestionType, Difficulty, ExpectedSectionReferences, GoldAnswer, IracReasoningJson`), `BenchmarkDataset` (record: `Questions, SourcePath, SkippedRowCount`), `BenchmarkDatasetRow` / `BenchmarkPossibleSectionRow` (System.Text.Json raw-row classes with exact Hugging Face column names), `BenchmarkRunOptions`, `BenchmarkQuestionScoreDto`, `BenchmarkCategoryResultDto`, `BenchmarkRunResultDto` — Tasks 2, 4, and 6 depend on these exact names and shapes.

- [ ] **Step 1: Create the `BenchmarkPromptVariant` enum**

```csharp
namespace MuktoAin.Domain.Enums;

// S-3.2/S-3.3: which prompt-assembly strategy the benchmark runner drives.
// FewShotIrac is added by S-3.3 (Task 6 of the QA benchmark chain plan).
public enum BenchmarkPromptVariant
{
    ZeroShot = 0
}
```

- [ ] **Step 2: Create the canonical question DTO and the raw dataset-row DTO**

```csharp
namespace MuktoAin.Application.DTOs;

// One canonical benchmark question, mapped from a raw
// momahadi/bangladesh-legal-qa-dataset row (schema: data/README.md §2.2).
public sealed record BenchmarkQuestionDto(
    int DatasetId,
    string Question,
    string Language,
    string Category,
    string QuestionType,
    string Difficulty,
    IReadOnlyList<string> ExpectedSectionReferences,
    string? GoldAnswer,
    string? IracReasoningJson);
```

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MuktoAin.Application.DTOs;

// Raw JSON row of the QA benchmark dataset. Property names match the published
// Hugging Face schema of momahadi/bangladesh-legal-qa-dataset verbatim (the
// dataset card uses spaces and mixed casing in column names), so the
// git-ignored downloaded file deserializes without any rename pass.
public sealed class BenchmarkPossibleSectionRow
{
    [JsonPropertyName("Section Number")]
    public string? SectionNumber { get; set; }

    [JsonPropertyName("Full Text")]
    public string? FullText { get; set; }
}

public sealed class BenchmarkDatasetRow
{
    [JsonPropertyName("dataset_id")]
    public int DatasetId { get; set; }

    [JsonPropertyName("Act")]
    public string? Act { get; set; }

    [JsonPropertyName("Entry_ID")]
    public int EntryId { get; set; }

    [JsonPropertyName("question_type")]
    public string? QuestionType { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("Question")]
    public string? Question { get; set; }

    [JsonPropertyName("Question_No")]
    public string? QuestionNo { get; set; }

    [JsonPropertyName("Correct_Option")]
    public string? CorrectOption { get; set; }

    [JsonPropertyName("Possible Sections")]
    public List<BenchmarkPossibleSectionRow>? PossibleSections { get; set; }

    [JsonPropertyName("Relevant Section")]
    public string? RelevantSection { get; set; }

    [JsonPropertyName("Section Number")]
    public string? SectionNumber { get; set; }

    [JsonPropertyName("Subsection/Clause")]
    public string? SubsectionClause { get; set; }

    [JsonPropertyName("Section Text")]
    public string? SectionText { get; set; }

    // Kept as raw JSON (dict with variable keys, e.g. "Rule": { "Section 2(2)": "..." }).
    [JsonPropertyName("IRAC_Reasoning")]
    public JsonElement? IracReasoning { get; set; }

    [JsonPropertyName("Answer")]
    public string? Answer { get; set; }

    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("Difficulty")]
    public string? Difficulty { get; set; }

    [JsonPropertyName("Keywords")]
    public List<string>? Keywords { get; set; }

    [JsonPropertyName("Cited Acts and Sections")]
    public string? CitedActsAndSections { get; set; }

    [JsonPropertyName("source_file")]
    public string? SourceFile { get; set; }

    [JsonPropertyName("quality_flag")]
    public bool QualityFlag { get; set; }

    [JsonPropertyName("quality_flag_reason")]
    public string? QualityFlagReason { get; set; }
}
```

- [ ] **Step 3: Create the dataset, run-options, and result DTOs**

```csharp
namespace MuktoAin.Application.DTOs;

public sealed record BenchmarkDataset(
    IReadOnlyList<BenchmarkQuestionDto> Questions,
    string SourcePath,
    int SkippedRowCount);
```

```csharp
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.DTOs;

public sealed record BenchmarkRunOptions
{
    public BenchmarkPromptVariant Variant { get; init; } = BenchmarkPromptVariant.ZeroShot;

    // Null/absent = run the whole dataset.
    public int? MaxQuestions { get; init; }

    // Null = auto-resolve (full dataset, falling back to the committed sample).
    public string? DatasetPath { get; init; }

    // Null = data/benchmark/results/<variant>.json under the repo root.
    public string? OutputPath { get; init; }

    public int TopK { get; init; } = 8;

    // Gemini free-tier pacing for full 2,165-question runs (e.g. 500).
    public int DelayBetweenQuestionsMs { get; init; }
}
```

```csharp
namespace MuktoAin.Application.DTOs;

public sealed class BenchmarkQuestionScoreDto
{
    public int DatasetId { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    // Final response (disclaimer already injected by DisclaimerInjector).
    public string Response { get; set; } = string.Empty;

    // Non-null => this question failed mid-pipeline and was skipped.
    public string? Error { get; set; }

    public List<string> CitedReferences { get; set; } = new();

    public List<string> ExpectedReferences { get; set; } = new();

    public double Precision { get; set; }

    public double Recall { get; set; }

    public double F1 { get; set; }
}
```

```csharp
namespace MuktoAin.Application.DTOs;

public sealed class BenchmarkCategoryResultDto
{
    public string Category { get; set; } = string.Empty;

    public int QuestionCount { get; set; }

    public double MeanPrecision { get; set; }

    public double MeanRecall { get; set; }

    public double MeanF1 { get; set; }
}
```

```csharp
namespace MuktoAin.Application.DTOs;

// One benchmark run (zero-shot or few-shot) — serialized to
// data/benchmark/results/*.json and re-read by CompareAsync (Task 6), hence
// plain settable properties rather than positional records.
public sealed class BenchmarkRunResultDto
{
    public string Variant { get; set; } = string.Empty;

    public string DatasetPath { get; set; } = string.Empty;

    public string OutputPath { get; set; } = string.Empty;

    public int TotalQuestions { get; set; }

    public int ScoredQuestions { get; set; }

    public int SkippedQuestions { get; set; }

    public double MeanPrecision { get; set; }

    public double MeanRecall { get; set; }

    public double MeanF1 { get; set; }

    public List<BenchmarkCategoryResultDto> Categories { get; set; } = new();

    public List<BenchmarkQuestionScoreDto> Questions { get; set; } = new();

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }
}
```

- [ ] **Step 4: Create the committed seed sample `data/benchmark/benchmark-sample.json`**

Five rows mirroring the published schema: 2 Bangla bar-exam MCQs, 1 English MCQ, 1 English scenario row, and 1 row with an empty `Question` (exercises skip logic). Rows 1 and 3 carry the full schema; rows 2 and 5 omit optional fields (`Possible Sections`, `IRAC_Reasoning`, `Section Text`) to prove tolerance.

```json
[
  {
    "dataset_id": 1,
    "Act": "The Code of Civil Procedure, 1908",
    "Entry_ID": 1,
    "question_type": "bar_exam",
    "language": "Bangla",
    "Question": "The Code of Civil Procedure, 1908 এর ২(২) ধারা অনুসারে 'ডিক্রি' (Decree) এর অন্তর্ভুক্ত হবে নিচের কোনটি? (ক) মধ্যবর্তী লাভের (Mesne profits) আদেশ (খ) আদেশ হিসেবে আপিলযোগ্য কোনো সিদ্ধান্ত (গ) ডিফল্টের কারণে খারিজ করার কোনো আদেশ (ঘ) আরজি (Plaint) প্রত্যাখ্যান বা খারিজ করা",
    "Question_No": "২",
    "Correct_Option": "ঘ",
    "Possible Sections": [
      {
        "Section Number": "The Code of Civil Procedure, 1908, Section 2(2)",
        "Full Text": "\"ডিক্রি\" অর্থ একটি বিচারিক সিদ্ধান্তের আনুষ্ঠানিক অভিব্যক্তি যা পক্ষগুলির অধিকার চূড়ান্তভাবে নির্ধারণ করে।"
      }
    ],
    "Relevant Section": "The Code of Civil Procedure, 1908, Section 2(2)",
    "Section Number": "Section 2",
    "Subsection/Clause": "(২)",
    "Section Text": "\"ডিক্রি\" অর্থ একটি বিচারিক সিদ্ধান্তের আনুষ্ঠানিক অভিব্যক্তি যা পক্ষগুলির অধিকার চূড়ান্তভাবে নির্ধারণ করে।",
    "IRAC_Reasoning": {
      "Issue": "ডিক্রির সংজ্ঞার অধীনে আরজি প্রত্যাখ্যান অন্তর্ভুক্ত কি না?",
      "Rule": { "Section 2(2)": "ডিক্রির সংজ্ঞায় প্লেইন্ট বা আরজি খারিজকে অন্তর্ভুক্ত করা হয়েছে।" },
      "Application": "ধারা ২(২) সরাসরি প্রযোজ্য।",
      "Conclusion": "সঠিক উত্তর (ঘ)।"
    },
    "Answer": "সঠিক উত্তর হলো (ঘ) আরজি (Plaint) প্রত্যাখ্যান বা খারিজ করা।",
    "Type": "T03 — Definition / Concept Identification",
    "Difficulty": "High",
    "Keywords": ["ডিক্রি", "আরজি প্রত্যাখ্যান", "আইনি সংজ্ঞা"],
    "Cited Acts and Sections": "The Code of Civil Procedure, 1908, Section 2(2)",
    "source_file": "Q-As/Bangla/Code of civil procedure",
    "quality_flag": false,
    "quality_flag_reason": ""
  },
  {
    "dataset_id": 2,
    "Act": "Bangladesh Labour Act, 2006",
    "Entry_ID": 7,
    "question_type": "bar_exam",
    "language": "English",
    "Question": "Under the Bangladesh Labour Act, 2006, within how many working days after the end of the wage period must a worker's wages be paid? (a) 3 (b) 5 (c) 7 (d) 10",
    "Question_No": "৭",
    "Correct_Option": "c",
    "Relevant Section": "Bangladesh Labour Act, 2006, Section 123",
    "Section Number": "Section 123",
    "Subsection/Clause": "(1)",
    "Answer": "(c) 7 working days, per Section 123 of the Bangladesh Labour Act, 2006.",
    "Type": "T05 — Numerical / Quantitative",
    "Difficulty": "Medium",
    "Keywords": ["wages", "payment deadline", "wage period"],
    "Cited Acts and Sections": "Bangladesh Labour Act, 2006, Section 123",
    "source_file": "Q-As/English/Bangladesh labour act",
    "quality_flag": false,
    "quality_flag_reason": ""
  },
  {
    "dataset_id": 3,
    "Act": "The Code of Civil Procedure, 1908",
    "Entry_ID": 5,
    "question_type": "bar_exam",
    "language": "English",
    "Question": "A, residing in Chattogram, goes to Dhaka and beats B. Where may B sue A? (a) Chattogram only (b) Dhaka or Chattogram, at B's option (c) Dhaka only (d) The High Court Division only",
    "Question_No": "৫",
    "Correct_Option": "b",
    "Possible Sections": [
      {
        "Section Number": "The Code of Civil Procedure, 1908, Section 19",
        "Full Text": "Where compensation is claimed for wrong to person or movable property, if the wrong was done within the local limits of one court's jurisdiction and the defendant resides or carries on business within the limits of another, the suit may be instituted in either court."
      }
    ],
    "Relevant Section": "The Code of Civil Procedure, 1908, Section 19",
    "Section Number": "Section 19",
    "Subsection/Clause": "Illustration (ক)",
    "Section Text": "যখন কোন মামলা ব্যক্তির ক্ষতির জন্য ক্ষতিপূরণের জন্য দায়ের করা হয়, যদি ক্ষতি এক আদালতের এখতিয়ারে এবং বিবাদী অন্য আদালতের এখতিয়ারে বসবাস করে, তখন মামলা বাদীর অপশনে যে কোনো একটি আদালতে দায়ের করা যাবে।",
    "IRAC_Reasoning": {
      "Issue": "ব্যক্তির ক্ষতির ক্ষেত্রে মামলা দায়েরের সঠিক স্থান কোনটি?",
      "Rule": { "Section 19": "ব্যক্তি বা চল সম্পত্তির ক্ষতির মামলা যেখানে ক্ষতি হয়েছে অথবা বিবাদী বসবাস করে, সেখানে দায়ের করা যায়।" },
      "Application": "প্রহার ঢাকায় হয়েছে, বিবাদী চট্টগ্রামে থাকে — উভয় স্থানই বৈধ।",
      "Conclusion": "সঠিক উত্তর (খ)।"
    },
    "Answer": "(b) Dhaka or Chattogram, at B's option, per Section 19 of the Code of Civil Procedure, 1908.",
    "Type": "T04 — Scenario / Fact Pattern Application",
    "Difficulty": "High",
    "Keywords": ["jurisdiction", "personal injury", "place of suing"],
    "Cited Acts and Sections": "The Code of Civil Procedure, 1908, Section 19",
    "source_file": "Q-As/English/Code of civil procedure",
    "quality_flag": false,
    "quality_flag_reason": ""
  },
  {
    "dataset_id": 4,
    "Act": "Bangladesh Labour Act, 2006",
    "Entry_ID": 9,
    "question_type": "bar_exam",
    "language": "Bangla",
    "Question": "",
    "Question_No": "১১",
    "Correct_Option": "ক",
    "Relevant Section": "Bangladesh Labour Act, 2006, Section 150",
    "Answer": "",
    "source_file": "Q-As/Bangla/Bangladesh labour act",
    "quality_flag": true,
    "quality_flag_reason": "Empty question — intentionally malformed seed row"
  },
  {
    "dataset_id": 5,
    "Act": "Bangladesh Labour Act, 2006",
    "Entry_ID": 10,
    "question_type": "bar_exam",
    "language": "Bangla",
    "Question": "কর্মক্ষেত্রে দুর্ঘটনায় আহত হলে ক্ষতিপূরণের জন্য বাংলাদেশ শ্রম আইন, ২০০৬-এর কোন ধারা প্রযোজ্য?",
    "Question_No": "৯",
    "Correct_Option": "ক",
    "Possible Sections": [
      {
        "Section Number": "Bangladesh Labour Act, 2006, Section 150",
        "Full Text": "If personal injury is caused to a worker by accident arising out of and in the course of his employment, the employer shall be liable to pay compensation."
      }
    ],
    "Relevant Section": "Bangladesh Labour Act, 2006, Section 150",
    "Section Number": "Section 150",
    "Subsection/Clause": "(1)",
    "Section Text": "যদি কর্মক্ষেত্রে দুর্ঘটনাজনিত আঘাতে কর্মচারী আহত হয়, তবে নিয়োগকর্তা ক্ষতিপূরণ প্রদানে বাধ্য থাকবেন।",
    "IRAC_Reasoning": {
      "Issue": "কর্মক্ষেত্রে দুর্ঘটনায় আহত হলে ক্ষতিপূরণ পাওয়া যাবে কি না?",
      "Rule": { "Section 150": "কর্মের সময়ে দুর্ঘটনাজনিত আঘাতের জন্য নিয়োগকর্তা ক্ষতিপূরণ দিতে বাধ্য।" },
      "Application": "আঘাতটি কর্মের সময়ে ও কর্মক্ষেত্রে হয়েছে।",
      "Conclusion": "১৫০ ধারা প্রযোজ্য।"
    },
    "Answer": "১৫০ ধারা প্রযোজ্য — কর্মক্ষেত্রে দুর্ঘটনাজনিত আঘাতের ক্ষতিপূরণ।",
    "Type": "T12 — Remedy Identification",
    "Difficulty": "Medium",
    "Keywords": ["ক্ষতিপূরণ", "দুর্ঘটনা", "শ্রম আইন"],
    "Cited Acts and Sections": "Bangladesh Labour Act, 2006, Section 150",
    "source_file": "Q-As/Bangla/Bangladesh labour act",
    "quality_flag": true,
    "quality_flag_reason": "Reviewed"
  }
]
```

- [ ] **Step 4b: Update `data/README.md` §2.2 with the schema note**

Append to the end of section 2.2 in `data/README.md` (after the existing Description line):

```markdown
- **Schema:** Rows mirror the published Hugging Face schema (`dataset_id`, `Act`, `Entry_ID`, `question_type`, `language`, `Question`, `Correct_Option`, `Possible Sections[]`, `Relevant Section`, `Section Number`, `Subsection/Clause`, `Section Text`, `IRAC_Reasoning`, `Answer`, `Type`, `Difficulty`, `Keywords`, `Cited Acts and Sections`, `source_file`, `quality_flag`, `quality_flag_reason`). The canonical C# mapping lives in `src/MuktoAin.Application/DTOs/BenchmarkDatasetRow.cs`; a committed 5-row sample with the same shape is at `data/benchmark/benchmark-sample.json` and is the loader's fallback when the full file is absent.
```

- [ ] **Step 5: Build to verify the DTOs compile**

Run: `dotnet build src/MuktoAin.Application/MuktoAin.Application.csproj`
Expected: Build succeeded with 0 errors.

---

### Task 2: S-3.1 — `BenchmarkLoaderService` (Dataset Loader)

**Files:**
- Create: `src/MuktoAin.Application/Services/BenchmarkDataPathResolver.cs`
- Create: `src/MuktoAin.Application/Services/IBenchmarkLoader.cs`
- Create: `src/MuktoAin.Application/Services/BenchmarkLoaderService.cs`
- Test: `tests/MuktoAin.UnitTests/Services/BenchmarkLoaderServiceTests.cs`
- Modify: `plans/Dependency_plan.md:145` (flip S-3.1)

**Interfaces:**
- Consumes: `BenchmarkQuestionDto`, `BenchmarkDatasetRow`, `BenchmarkDataset` (Task 1); `BenchmarkScorer.ParseReference` (Task 3 — used only in the act-name fallback when a row has no `Act` field).
- Produces: `IBenchmarkLoader.LoadAsync(string? datasetPath = null, CancellationToken ct = default) : Task<BenchmarkDataset>` and concrete `BenchmarkLoaderService` — consumed by `BenchmarkRunnerService` (Task 4). Resolver: `BenchmarkDataPathResolver.TryResolve(string startDirectory, string relativePathUnderData, out string path) : bool` and `ResolveResultsOutputPath(string startDirectory, string relativePathUnderData) : string` (also consumed by Task 4).

- [ ] **Step 1: Write the failing loader tests**

```csharp
using System.Text.Json;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;

namespace MuktoAin.UnitTests.Services;

public class BenchmarkLoaderServiceTests
{
    private readonly BenchmarkLoaderService _loader = new();

    private static string WriteTempDataset(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"muktoain-benchmark-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task LoadAsync_Parses_HuggingFace_Schema_And_Maps_Fields()
    {
        const string json = """
            [
              {
                "dataset_id": 42,
                "Act": "Bangladesh Labour Act, 2006",
                "Entry_ID": 7,
                "question_type": "bar_exam",
                "language": "English",
                "Question": "  Within how many days must wages be paid? (a) 3 (b) 7  ",
                "Correct_Option": "b",
                "Relevant Section": "Bangladesh Labour Act, 2006, Section 123",
                "Answer": "7 working days.",
                "Difficulty": "Medium",
                "IRAC_Reasoning": { "Issue": "Late wages?", "Rule": { "Section 123": "7 days." } }
              }
            ]
            """;
        var path = WriteTempDataset(json);
        try
        {
            var dataset = await _loader.LoadAsync(path);

            Assert.Equal(path, dataset.SourcePath);
            Assert.Equal(0, dataset.SkippedRowCount);
            var q = Assert.Single(dataset.Questions);
            Assert.Equal(42, q.DatasetId);
            Assert.Equal("Within how many days must wages be paid? (a) 3 (b) 7", q.Question);
            Assert.Equal("en", q.Language);
            Assert.Equal("Bangladesh Labour Act, 2006", q.Category);
            Assert.Equal("bar_exam", q.QuestionType);
            Assert.Equal("Medium", q.Difficulty);
            Assert.Equal(new[] { "Bangladesh Labour Act, 2006, Section 123" }, q.ExpectedSectionReferences);
            Assert.Equal("7 working days.", q.GoldAnswer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_Normalizes_Language_Bangla_Defaults_To_Bn()
    {
        const string json = """
            [
              { "dataset_id": 1, "language": "Bangla", "Question": "প্রশ্ন?", "Relevant Section": "Act X, Section 1" },
              { "dataset_id": 2, "language": "English", "Question": "Q?", "Relevant Section": "Act X, Section 2" },
              { "dataset_id": 3, "language": "", "Question": "Q3?", "Relevant Section": "Act X, Section 3" }
            ]
            """;
        var path = WriteTempDataset(json);
        try
        {
            var dataset = await _loader.LoadAsync(path);

            Assert.Equal("bn", dataset.Questions[0].Language);
            Assert.Equal("en", dataset.Questions[1].Language);
            Assert.Equal("bn", dataset.Questions[2].Language);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_Skips_Rows_Without_Question_Or_Gold_Reference()
    {
        const string json = """
            [
              { "dataset_id": 1, "Question": "", "Relevant Section": "Act X, Section 1" },
              { "dataset_id": 2, "Question": "Valid?", "Relevant Section": "Act X, Section 2" },
              { "dataset_id": 3, "Question": "No gold?" },
              { "dataset_id": 4, "Question": "  ", "Relevant Section": "Act X, Section 4" }
            ]
            """;
        var path = WriteTempDataset(json);
        try
        {
            var dataset = await _loader.LoadAsync(path);

            var q = Assert.Single(dataset.Questions);
            Assert.Equal(2, q.DatasetId);
            Assert.Equal(3, dataset.SkippedRowCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_Tolerates_Missing_Optional_Fields_And_Derives_Fallbacks()
    {
        const string json = """
            [
              { "dataset_id": 9, "Question": "Minimal row?", "Relevant Section": "The Code of Civil Procedure, 1908, Section 9" }
            ]
            """;
        var path = WriteTempDataset(json);
        try
        {
            var dataset = await _loader.LoadAsync(path);

            var q = Assert.Single(dataset.Questions);
            Assert.Equal(string.Empty, q.QuestionType);
            Assert.Equal(string.Empty, q.Difficulty);
            Assert.Null(q.GoldAnswer);
            Assert.Null(q.IracReasoningJson);
            Assert.Equal("The Code of Civil Procedure, 1908", q.Category); // derived from the gold reference
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_Serializes_Irac_Reasoning_Back_To_Json()
    {
        const string json = """
            [
              { "dataset_id": 1, "Question": "Q?", "Relevant Section": "Act X, Section 1",
                "IRAC_Reasoning": { "Issue": "I", "Rule": { "S1": "R" }, "Application": "A", "Conclusion": "C" } }
            ]
            """;
        var path = WriteTempDataset(json);
        try
        {
            var dataset = await _loader.LoadAsync(path);

            var q = Assert.Single(dataset.Questions);
            Assert.NotNull(q.IracReasoningJson);
            using var doc = JsonDocument.Parse(q.IracReasoningJson!);
            Assert.Equal("I", doc.RootElement.GetProperty("Issue").GetString());
            Assert.Equal("C", doc.RootElement.GetProperty("Conclusion").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_Accepts_Object_Root_With_Questions_Array()
    {
        const string json = """
            { "questions": [ { "dataset_id": 5, "Question": "Wrapped?", "Relevant Section": "Act X, Section 5" } ] }
            """;
        var path = WriteTempDataset(json);
        try
        {
            var dataset = await _loader.LoadAsync(path);

            Assert.Equal(5, Assert.Single(dataset.Questions).DatasetId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_Explicit_Path_Missing_Throws_FileNotFound()
    {
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => _loader.LoadAsync(Path.Combine(Path.GetTempPath(), "definitely-missing-benchmark.json")));

        Assert.Contains("explicit path", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_Without_Path_Falls_Back_To_Committed_Sample()
    {
        // The unit-test project sits under the repo root, so the walk-up
        // resolver finds data/benchmark/benchmark-sample.json (committed in Task 1).
        var dataset = await _loader.LoadAsync();

        Assert.True(dataset.Questions.Count > 0);
        Assert.EndsWith("benchmark-sample.json", dataset.SourcePath, StringComparison.OrdinalIgnoreCase);
        // The committed sample's row 4 has an empty Question and must be skipped.
        Assert.Equal(1, dataset.SkippedRowCount);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~BenchmarkLoaderServiceTests"`
Expected: FAIL — compilation error `BenchmarkLoaderService` / `IBenchmarkLoader` / `BenchmarkDataPathResolver` not found.

- [ ] **Step 3: Create `BenchmarkDataPathResolver`**

`SeedDataPathResolver` (src/MuktoAin.Infrastructure/Data/Seeding/SeedDataPathResolver.cs) is `internal` to Infrastructure, so Application gets its own copy with a deeper walk: `AppContext.BaseDirectory` in test projects sits ~7 levels below the repo root (`bin/Debug/net8.0`), versus the Web project's 2.

```csharp
namespace MuktoAin.Application.Services;

// Resolves files under the repo root's `data/` directory, mirroring
// Infrastructure's SeedDataPathResolver but starting from AppContext.BaseDirectory,
// which is much deeper (tests/*/bin/Debug/net8.0) — hence MaxLevelsUp 8, not 4.
internal static class BenchmarkDataPathResolver
{
    private const int MaxLevelsUp = 8;

    public static bool TryResolve(string startDirectory, string relativePathUnderData, out string path)
    {
        var dir = new DirectoryInfo(startDirectory);
        for (var i = 0; i < MaxLevelsUp && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data", relativePathUnderData);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    // For benchmark result outputs: anchors on the first existing `data/` directory
    // above the caller and creates the target subdirectory if needed.
    public static string ResolveResultsOutputPath(string startDirectory, string relativePathUnderData)
    {
        var dir = new DirectoryInfo(startDirectory);
        for (var i = 0; i < MaxLevelsUp && dir is not null; i++, dir = dir.Parent)
        {
            var dataDir = Path.Combine(dir.FullName, "data");
            if (!Directory.Exists(dataDir)) continue;

            var fullPath = Path.Combine(dataDir, relativePathUnderData);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return fullPath;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate a 'data' directory at or above '{startDirectory}'.");
    }
}
```

- [ ] **Step 4: Create `IBenchmarkLoader` and `BenchmarkLoaderService`**

```csharp
using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

public interface IBenchmarkLoader
{
    Task<BenchmarkDataset> LoadAsync(string? datasetPath = null, CancellationToken ct = default);
}
```

```csharp
using System.Text.Json;
using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

// S-3.1: parses the QA benchmark dataset (momahadi/bangladesh-legal-qa-dataset,
// 2,165 rows, CC BY 4.0 — data/README.md §2.2, docs/attribution-CC-BY-SA-4.0.md §2)
// into canonical BenchmarkQuestionDto records. Resolution order: explicit path →
// data/bangladesh-legal-qa-dataset.json (git-ignored full download) → committed
// data/benchmark/benchmark-sample.json seed.
public class BenchmarkLoaderService : IBenchmarkLoader
{
    internal const string PrimaryFileName = "bangladesh-legal-qa-dataset.json";
    internal const string SampleRelativePath = "benchmark/benchmark-sample.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<BenchmarkDataset> LoadAsync(string? datasetPath = null, CancellationToken ct = default)
    {
        var path = ResolveDatasetPath(datasetPath);
        var json = await File.ReadAllTextAsync(path, ct);
        using var doc = JsonDocument.Parse(json);

        var rows = ExtractRows(doc.RootElement);
        var questions = new List<BenchmarkQuestionDto>(rows.Count);
        var skipped = 0;

        foreach (var row in rows)
        {
            var question = MapRow(row);
            if (question is null) skipped++;
            else questions.Add(question);
        }

        return new BenchmarkDataset(questions, path, skipped);
    }

    internal static string ResolveDatasetPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException(
                    $"Benchmark dataset not found at explicit path '{explicitPath}'.", explicitPath);
            }

            return explicitPath;
        }

        if (BenchmarkDataPathResolver.TryResolve(AppContext.BaseDirectory, PrimaryFileName, out var primary))
        {
            return primary;
        }

        if (BenchmarkDataPathResolver.TryResolve(AppContext.BaseDirectory, SampleRelativePath, out var sample))
        {
            return sample;
        }

        throw new FileNotFoundException(
            "Benchmark dataset not found. Download the dataset (see data/README.md §2.2) to " +
            "data/bangladesh-legal-qa-dataset.json, or pass an explicit path to LoadAsync. " +
            "The committed sample data/benchmark/benchmark-sample.json was not found either.");
    }

    internal static List<BenchmarkDatasetRow> ExtractRows(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<BenchmarkDatasetRow>>(root.GetRawText(), JsonOptions)
                ?? throw new InvalidOperationException("Benchmark dataset array deserialized to no rows.");
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "questions", "data", "rows" })
            {
                if (root.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<BenchmarkDatasetRow>>(array.GetRawText(), JsonOptions)
                        ?? throw new InvalidOperationException(
                            $"Benchmark dataset '{propertyName}' array deserialized to no rows.");
                }
            }
        }

        throw new InvalidOperationException(
            "Unrecognized benchmark dataset shape: expected a JSON array or an object with a 'questions'/'data'/'rows' array.");
    }

    internal static BenchmarkQuestionDto? MapRow(BenchmarkDatasetRow? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Question)) return null;

        var expected = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.RelevantSection)) expected.Add(row.RelevantSection.Trim());
        if (!string.IsNullOrWhiteSpace(row.CitedActsAndSections) &&
            !expected.Contains(row.CitedActsAndSections.Trim()))
        {
            expected.Add(row.CitedActsAndSections.Trim());
        }

        if (expected.Count == 0) return null;

        return new BenchmarkQuestionDto(
            row.DatasetId != 0 ? row.DatasetId : row.EntryId,
            row.Question.Trim(),
            NormalizeLanguage(row.Language),
            string.IsNullOrWhiteSpace(row.Act) ? FallbackCategory(row.RelevantSection) : row.Act.Trim(),
            row.QuestionType?.Trim() ?? string.Empty,
            row.Difficulty?.Trim() ?? string.Empty,
            expected,
            string.IsNullOrWhiteSpace(row.Answer) ? null : row.Answer.Trim(),
            row.IracReasoning is { ValueKind: JsonValueKind.Object or JsonValueKind.Array }
                ? row.IracReasoning.Value.GetRawText()
                : null);
    }

    internal static string NormalizeLanguage(string? language) => language?.Trim().ToLowerInvariant() switch
    {
        "english" or "en" => "en",
        _ => "bn" // "Bangla", "বাংলা", empty, or unknown → Bangla (the dataset's majority language)
    };

    private static string FallbackCategory(string? relevantSection)
    {
        var parsed = BenchmarkScorer.ParseReference(relevantSection);
        return string.IsNullOrWhiteSpace(parsed.ActTitle) ? "Unknown" : parsed.ActTitle;
    }
}
```

- [ ] **Step 5: Run the loader tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~BenchmarkLoaderServiceTests"`
Expected: PASS (9 tests). Note: two tests (`LoadAsync_Tolerates_Missing_Optional_Fields_And_Derives_Fallbacks`, and the sample-fallback test) exercise `BenchmarkScorer.ParseReference` via the `Act`-less fallback path — if Task 3 has not been implemented yet, implement Task 3 first or temporarily expect "Unknown" as the category; the plan's task order (Task 3 next) resolves this.

- [ ] **Step 6: Run the full unit-test suite to check for regressions**

Run: `dotnet test tests/MuktoAin.UnitTests`
Expected: PASS — no existing tests touched.

- [ ] **Step 7: Verify the build across the solution**

Run: `dotnet build MuktoAin.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Record completion in plans/Dependency_plan.md**

Flip line 145 of `plans/Dependency_plan.md` from

```markdown
- [ ] **[S-3.1]** QA Benchmark Dataset Loader (2,165 Questions) — *Arpita* `[Blocked by: T-2.3, A-2.2] [Unblocks: S-3.2]`
```

to

```markdown
~~- [x] **[S-3.1]** QA Benchmark Dataset Loader (2,165 Questions) — *Arpita* `[Blocked by: T-2.3, A-2.2] [Unblocks: S-3.2]`~~
```

---

### Task 3: Reference Parsing, Matching, and Per-Question Scoring (`BenchmarkScorer`)

**Files:**
- Create: `src/MuktoAin.Application/Services/BenchmarkScorer.cs`
- Test: `tests/MuktoAin.UnitTests/Services/BenchmarkScorerTests.cs`

**Interfaces:**
- Consumes: `RetrievedSection` (src/MuktoAin.Domain/Models/RetrievedSection.cs — record with `SectionId, ActTitle, SectionNumber, SectionText, RelevanceScore, Method, ActNumber, ActYear`).
- Produces: `BenchmarkSectionReference` record (`ActTitle, SectionNumber`) and static `BenchmarkScorer` with `Normalize(string?) : string`, `ParseReference(string?) : BenchmarkSectionReference`, `BaseSectionNumber(string?) : string`, `IsMatch(BenchmarkSectionReference, RetrievedSection) : bool`, `ScoreQuestion(IReadOnlyList<string>, IReadOnlyList<RetrievedSection>) : (double Precision, double Recall, double F1)` — consumed by `BenchmarkLoaderService.FallbackCategory` (Task 2) and `BenchmarkRunnerService` (Task 4).

- [ ] **Step 1: Write the failing scorer tests**

```csharp
using MuktoAin.Application.Services;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Models;

namespace MuktoAin.UnitTests.Services;

public class BenchmarkScorerTests
{
    [Theory]
    [InlineData("The Code of Civil Procedure, 1908, Section 2(2)",
                "The Code of Civil Procedure, 1908", "2(2)")]
    [InlineData("Bangladesh Labour Act, 2006, ধারা ৩৫ক",
                "Bangladesh Labour Act, 2006", "৩৫ক")]
    public void ParseReference_Splits_Act_And_Section(string reference, string act, string section)
    {
        var parsed = BenchmarkScorer.ParseReference(reference);

        Assert.Equal(act, parsed.ActTitle);
        Assert.Equal(section, parsed.SectionNumber);
    }

    [Fact]
    public void ParseReference_Without_Separator_Returns_Whole_String_As_Act()
    {
        var parsed = BenchmarkScorer.ParseReference("The Penal Code, 1860");

        Assert.Equal("The Penal Code, 1860", parsed.ActTitle);
        Assert.Equal(string.Empty, parsed.SectionNumber);
    }

    [Fact]
    public void Normalize_MapsBanglaDigits_Lowercases_AndStripsPunctuation()
    {
        Assert.Equal("thecodeofcivilprocedure1908 er 2(2) dhara",
            BenchmarkScorer.Normalize("The Code of Civil Procedure, 1908 এর ২(২) ধারা"));
        Assert.Equal("wages under section 123", BenchmarkScorer.Normalize("Wages under Section 123!"));
        Assert.Equal(string.Empty, BenchmarkScorer.Normalize(null));
    }

    [Theory]
    [InlineData("2(2)", "2")]
    [InlineData("২(২)", "2")]
    [InlineData("35A", "35A")]
    [InlineData("৩৫ক", "35")]
    [InlineData("151", "151")]
    [InlineData("Section 152", "152")]
    public void BaseSectionNumber_Strips_Clauses_And_MapsDigits(string input, string expected)
    {
        Assert.Equal(expected, BenchmarkScorer.BaseSectionNumber(input));
    }

    [Fact]
    public void IsMatch_ActTitleContainment_And_SectionBaseEquality()
    {
        var expected = BenchmarkScorer.ParseReference("Bangladesh Labour Act, 2006, Section 123");
        var cited = new RetrievedSection(
            SectionId: 123,
            ActTitle: "Bangladesh Labour Act 2006",   // punctuation differs from gold
            SectionNumber: "123",
            SectionText: "The wages of every worker...",
            RelevanceScore: 0.9f,
            Method: RetrievalMethod.Vector);

        Assert.True(BenchmarkScorer.IsMatch(expected, cited));
    }

    [Fact]
    public void IsMatch_Different_Section_Number_Is_Not_A_Hit()
    {
        var expected = BenchmarkScorer.ParseReference("Bangladesh Labour Act, 2006, Section 123");
        var cited = new RetrievedSection(
            124, "Bangladesh Labour Act, 2006", "150", "...", 0.8f, RetrievalMethod.Vector);

        Assert.False(BenchmarkScorer.IsMatch(expected, cited));
    }

    [Fact]
    public void IsMatch_Different_Act_Is_Not_A_Hit()
    {
        var expected = BenchmarkScorer.ParseReference("Bangladesh Labour Act, 2006, Section 123");
        var cited = new RetrievedSection(
            123, "The Code of Civil Procedure, 1908", "123", "...", 0.8f, RetrievalMethod.Vector);

        Assert.False(BenchmarkScorer.IsMatch(expected, cited));
    }

    [Fact]
    public void ScoreQuestion_PerfectHit_Gives_Precision_Recall_F1_Of_One()
    {
        var cited = new List<RetrievedSection>
        {
            new(123, "Bangladesh Labour Act, 2006", "123", "...", 0.9f, RetrievalMethod.Vector)
        };

        var (precision, recall, f1) = BenchmarkScorer.ScoreQuestion(
            new[] { "Bangladesh Labour Act, 2006, Section 123" }, cited);

        Assert.Equal(1.0, precision);
        Assert.Equal(1.0, recall);
        Assert.Equal(1.0, f1);
    }

    [Fact]
    public void ScoreQuestion_PartialHit_Computes_Harmonic_Mean()
    {
        // One hit of two cited, one expected → P=0.5, R=1.0, F1=2*0.5*1/(1.5)=2/3
        var cited = new List<RetrievedSection>
        {
            new(123, "Bangladesh Labour Act, 2006", "123", "...", 0.9f, RetrievalMethod.Vector),
            new(150, "Bangladesh Labour Act, 2006", "150", "...", 0.7f, RetrievalMethod.Vector)
        };

        var (precision, recall, f1) = BenchmarkScorer.ScoreQuestion(
            new[] { "Bangladesh Labour Act, 2006, Section 123" }, cited);

        Assert.Equal(0.5, precision);
        Assert.Equal(1.0, recall);
        Assert.Equal(2.0 / 3.0, f1, precision: 4);
    }

    [Fact]
    public void ScoreQuestion_NoHits_Returns_Zeros()
    {
        var cited = new List<RetrievedSection>
        {
            new(150, "Bangladesh Labour Act, 2006", "150", "...", 0.7f, RetrievalMethod.Vector)
        };

        var (precision, recall, f1) = BenchmarkScorer.ScoreQuestion(
            new[] { "Bangladesh Labour Act, 2006, Section 123" }, cited);

        Assert.Equal(0, precision);
        Assert.Equal(0, recall);
        Assert.Equal(0, f1);
    }

    [Fact]
    public void ScoreQuestion_EmptyInputs_Returns_Zeros()
    {
        var (precision, recall, f1) = BenchmarkScorer.ScoreQuestion(
            Array.Empty<string>(), Array.Empty<RetrievedSection>());

        Assert.Equal(0, precision);
        Assert.Equal(0, recall);
        Assert.Equal(0, f1);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~BenchmarkScorerTests"`
Expected: FAIL — compilation error `BenchmarkScorer` not found.

- [ ] **Step 3: Implement `BenchmarkScorer`**

```csharp
using System.Text;
using System.Text.RegularExpressions;
using MuktoAin.Domain.Models;

namespace MuktoAin.Application.Services;

// A gold reference parsed out of a dataset row, e.g.
// "The Code of Civil Procedure, 1908, Section 2(2)".
public sealed record BenchmarkSectionReference(string ActTitle, string SectionNumber);

public static class BenchmarkScorer
{
    private static readonly Regex ReferenceRegex =
        new(@"^(?<act>.+?),\s*(?:Section|ধারা)\s*(?<sec>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Regex SectionBaseRegex =
        new(@"^(?<base>[0-9]+[A-Za-z]?)",
            RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Splits "Act Title, Section 2(2)" / "Act Title, ধারা ৩৫ক" into its two parts.
    /// Falls back to (whole string, "") when the ", Section"/", ধারা" separator is absent.
    /// </summary>
    public static BenchmarkSectionReference ParseReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new BenchmarkSectionReference(string.Empty, string.Empty);
        }

        var match = ReferenceRegex.Match(reference.Trim());
        return match.Success
            ? new BenchmarkSectionReference(match.Groups["act"].Value.Trim(), match.Groups["sec"].Value.Trim())
            : new BenchmarkSectionReference(reference.Trim(), string.Empty);
    }

    /// <summary>
    /// Folds Bangla digits to ASCII, lowercases, and keeps only letters/digits —
    /// used for act-title comparison so punctuation differences never break a match.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= '\u09E6' and <= '\u09EF')
            {
                sb.Append((char)('0' + (c - '\u09E6')));
            }
            else if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            // spaces, punctuation, and non-letter symbols are dropped
        }

        return sb.ToString();
    }

    /// <summary>
    /// Section-granularity key: "2(2)" → "2", "35A" → "35A", "৩৫ক" → "35".
    /// Citations in this repo operate at the section level
    /// (CASE_ACT_REFERENCE.SectionId), so clause suffixes are deliberately ignored.
    /// </summary>
    public static string BaseSectionNumber(string? sectionNumber)
    {
        if (string.IsNullOrWhiteSpace(sectionNumber)) return string.Empty;

        var digitMapped = MapBanglaDigits(sectionNumber.Trim());
        var match = SectionBaseRegex.Match(digitMapped);
        return match.Success ? match.Groups["base"].Value : string.Empty;
    }

    public static bool IsMatch(BenchmarkSectionReference expected, RetrievedSection cited)
    {
        var expectedAct = Normalize(expected.ActTitle);
        var citedAct = Normalize(cited.ActTitle);

        // Containment either way handles "Bangladesh Labour Act 2006" vs
        // "Bangladesh Labour Act, 2006" style punctuation differences.
        var actMatches = citedAct.Length > 0 &&
            (expectedAct.Contains(citedAct, StringComparison.Ordinal) ||
             citedAct.Contains(expectedAct, StringComparison.Ordinal));
        if (!actMatches) return false;

        var expectedBase = BaseSectionNumber(expected.SectionNumber);
        var citedBase = BaseSectionNumber(cited.SectionNumber);
        return expectedBase.Length > 0 &&
               string.Equals(expectedBase, citedBase, StringComparison.Ordinal);
    }

    /// <summary>
    /// Citation-level precision/recall/F1 for one benchmark question:
    /// precision = hits / cited, recall = hits / expected.
    /// </summary>
    public static (double Precision, double Recall, double F1) ScoreQuestion(
        IReadOnlyList<string> expectedReferences,
        IReadOnlyList<RetrievedSection> citedSections)
    {
        var expected = (expectedReferences ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(ParseReference)
            .ToList();
        var cited = citedSections ?? Array.Empty<RetrievedSection>();

        if (expected.Count == 0 || cited.Count == 0) return (0, 0, 0);

        var hits = cited.Count(c => expected.Any(e => IsMatch(e, c)));
        var precision = (double)hits / cited.Count;
        var recall = (double)hits / expected.Count;
        var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
        return (precision, recall, f1);
    }

    private static string MapBanglaDigits(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c is >= '\u09E6' and <= '\u09EF' ? (char)('0' + (c - '\u09E6')) : c);
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run the scorer tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~BenchmarkScorerTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Run the full unit-test suite (loader tests now fully green too)**

Run: `dotnet test tests/MuktoAin.UnitTests`
Expected: PASS.

---

### Task 4: S-3.2 — `BenchmarkRunnerService` (Zero-Shot Baseline)

**Files:**
- Create: `src/MuktoAin.Application/Services/IBenchmarkRunner.cs`
- Create: `src/MuktoAin.Application/Services/BenchmarkRunnerService.cs`
- Modify: `src/MuktoAin.Web/Program.cs` (after line 191, the S-2.2 `IAiOrchestrationService` registration)
- Modify: `src/MuktoAin.Web/Program.cs` (end of file — `public partial class Program`)
- Modify: `tests/MuktoAin.IntegrationTests/MuktoAin.IntegrationTests.csproj`
- Create: `tests/MuktoAin.IntegrationTests/AiPipeline/QaBenchmarkTests.cs`
- Test: `tests/MuktoAin.UnitTests/Services/BenchmarkRunnerServiceTests.cs`
- Modify: `plans/Dependency_plan.md:146` (flip S-3.2)

**Interfaces:**
- Consumes: `IBenchmarkLoader.LoadAsync` (Task 2), `BenchmarkScorer.ScoreQuestion` + `BenchmarkDataPathResolver` (Tasks 2–3), `IRagContextBuilder.RetrieveContextAsync(string query, int topK = 8)`, `IPromptAssembler.AssemblePromptAsync(string, IEnumerable<RetrievedSection>, string, AiRequestType, string?, CancellationToken)`, `MuktoAin.Domain.Interfaces.IAiService.GenerateContentAsync(string, CancellationToken)`, `DisclaimerInjector.InjectDisclaimer(string, string)` — all existing seams.
- Produces: `IBenchmarkRunner.RunAsync(BenchmarkRunOptions options, CancellationToken ct = default) : Task<BenchmarkRunResultDto>`; concrete `BenchmarkRunnerService(IBenchmarkLoader, IRagContextBuilder, IPromptAssembler, MuktoAin.Domain.Interfaces.IAiService, DisclaimerInjector)`; static `BenchmarkRunnerService.VariantLabel(BenchmarkPromptVariant) : string`. Task 6 adds `CompareAsync` to this interface. DI registers `IBenchmarkLoader` (singleton) + `IBenchmarkRunner` (scoped).

- [ ] **Step 1: Write the failing runner tests**

```csharp
using Moq;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.UnitTests.Services;

public class BenchmarkRunnerServiceTests
{
    private readonly Mock<IBenchmarkLoader> _loaderMock = new();
    private readonly Mock<IRagContextBuilder> _ragMock = new();
    private readonly Mock<IPromptAssembler> _promptMock = new();
    private readonly Mock<MuktoAin.Domain.Interfaces.IAiService> _aiMock = new();
    private readonly DisclaimerInjector _disclaimerInjector = new();

    private BenchmarkRunnerService CreateService() => new(
        _loaderMock.Object,
        _ragMock.Object,
        _promptMock.Object,
        _aiMock.Object,
        _disclaimerInjector);

    private static BenchmarkQuestionDto Question(int id, string category, string language = "en") => new(
        DatasetId: id,
        Question: $"Question {id} about unpaid wages?",
        Language: language,
        Category: category,
        QuestionType: "bar_exam",
        Difficulty: "High",
        ExpectedSectionReferences: new List<string> { "Bangladesh Labour Act, 2006, Section 123" },
        GoldAnswer: "Section 123.",
        IracReasoningJson: null);

    private static RetrievedSection HitSection() => new(
        123, "Bangladesh Labour Act, 2006", "123", "Wages...", 0.9f, RetrievalMethod.Vector);

    private static RetrievedSection MissSection() => new(
        150, "Bangladesh Labour Act, 2006", "150", "Accident...", 0.7f, RetrievalMethod.Vector);

    private void SetupHappyPath(params BenchmarkQuestionDto[] questions)
    {
        _loaderMock
            .Setup(l => l.LoadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BenchmarkDataset(questions, "dataset.json", 0));
        _ragMock
            .Setup(r => r.RetrieveContextAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<RetrievedSection> { HitSection(), MissSection() });
        _promptMock
            .Setup(p => p.AssemblePromptAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<RetrievedSection>>(), It.IsAny<string>(),
                AiRequestType.RightsExplanation, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ZERO-SHOT PROMPT");
        _aiMock
            .Setup(a => a.GenerateContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Grounded answer text.");
    }

    [Fact]
    public async Task RunAsync_ZeroShot_RunsPipeline_ScoresAndWritesJson()
    {
        SetupHappyPath(Question(1, "Bangladesh Labour Act, 2006"), Question(2, "The Code of Civil Procedure, 1908"));
        var outputPath = Path.Combine(Path.GetTempPath(), $"bench-run-{Guid.NewGuid():N}", "zero-shot.json");
        var service = CreateService();

        var result = await service.RunAsync(new BenchmarkRunOptions { OutputPath = outputPath });

        // Each question: 1 hit of 2 cited, 1 expected → P=0.5, R=1, F1=2/3. Mean F1 = 2/3.
        Assert.Equal(2, result.TotalQuestions);
        Assert.Equal(2, result.ScoredQuestions);
        Assert.Equal(0, result.SkippedQuestions);
        Assert.Equal("zero-shot", result.Variant);
        Assert.Equal(0.5, result.MeanPrecision);
        Assert.Equal(1.0, result.MeanRecall);
        Assert.Equal(2.0 / 3.0, result.MeanF1, precision: 4);
        Assert.All(result.Questions, q => Assert.Contains(Disclaimers.Legal, q.Response));
        Assert.Equal(2, result.Categories.Count);
        var labour = result.Categories.Single(c => c.Category == "Bangladesh Labour Act, 2006");
        Assert.Equal(1, labour.QuestionCount);
        Assert.Equal(2.0 / 3.0, labour.MeanF1, precision: 4);
        Assert.Equal(outputPath, result.OutputPath);
        Assert.True(File.Exists(outputPath));

        var json = File.ReadAllText(outputPath);
        Assert.Contains("\"Variant\": \"zero-shot\"", json);
        File.Delete(outputPath);
        Directory.Delete(Path.GetDirectoryName(outputPath)!);
    }

    [Fact]
    public async Task RunAsync_WhenRetrievalThrows_RecordsErrorAndContinues()
    {
        var q1 = Question(1, "Cat A");
        var q2 = Question(2, "Cat B");
        SetupHappyPath(q1, q2);
        _ragMock.SetupSequence(r => r.RetrieveContextAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new HttpRequestException("Qdrant down"))
            .ReturnsAsync(new List<RetrievedSection> { HitSection() });
        var service = CreateService();

        var result = await service.RunAsync(new BenchmarkRunOptions
        {
            OutputPath = Path.Combine(Path.GetTempPath(), $"bench-{Guid.NewGuid():N}.json")
        });

        Assert.Equal(2, result.TotalQuestions);
        Assert.Equal(1, result.ScoredQuestions);
        Assert.Equal(1, result.SkippedQuestions);
        Assert.NotNull(result.Questions[0].Error);
        Assert.Contains("Qdrant down", result.Questions[0].Error);
        Assert.Null(result.Questions[1].Error);
        Assert.Equal(1.0, result.MeanF1); // only the surviving question is scored
    }

    [Fact]
    public async Task RunAsync_MaxQuestions_Limits_The_Dataset()
    {
        SetupHappyPath(Question(1, "A"), Question(2, "B"), Question(3, "C"));
        var service = CreateService();

        var result = await service.RunAsync(new BenchmarkRunOptions { MaxQuestions = 2 });

        Assert.Equal(2, result.TotalQuestions);
        Assert.Equal(new[] { 1, 2 }, result.Questions.Select(q => q.DatasetId));
    }

    [Fact]
    public async Task RunAsync_UnknownVariant_Throws_ArgumentOutOfRange()
    {
        SetupHappyPath(Question(1, "A"));
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.RunAsync(new BenchmarkRunOptions { Variant = (BenchmarkPromptVariant)99 }));
    }

    [Fact]
    public async Task RunAsync_LoaderSkippedRows_Count_Toward_SkippedQuestions()
    {
        var q = Question(1, "A");
        SetupHappyPath(q);
        _loaderMock
            .Setup(l => l.LoadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BenchmarkDataset(new[] { q }, "sample.json", SkippedRowCount: 3));
        var service = CreateService();

        var result = await service.RunAsync(new BenchmarkRunOptions
        {
            OutputPath = Path.Combine(Path.GetTempPath(), $"bench-{Guid.NewGuid():N}.json")
        });

        Assert.Equal(1, result.ScoredQuestions);
        Assert.Equal(3, result.SkippedQuestions);
    }

    [Fact]
    public void VariantLabel_Maps_Enum_To_Report_Names()
    {
        Assert.Equal("zero-shot", BenchmarkRunnerService.VariantLabel(BenchmarkPromptVariant.ZeroShot));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~BenchmarkRunnerServiceTests"`
Expected: FAIL — compilation error `IBenchmarkRunner` / `BenchmarkRunnerService` not found.

- [ ] **Step 3: Implement `IBenchmarkRunner` and `BenchmarkRunnerService`**

```csharp
using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

public interface IBenchmarkRunner
{
    Task<BenchmarkRunResultDto> RunAsync(BenchmarkRunOptions options, CancellationToken ct = default);
}
```

```csharp
using System.Text.Json;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.Application.Services;

// S-3.2: zero-shot baseline QA benchmark runner. For each dataset question it
// mirrors AiOrchestrationService.ProcessCaseAsync's pipeline (retrieve →
// assemble → generate → inject disclaimer) WITHOUT the DB-facing parts — no
// AI_LOG rows, no CaseActReference persistence, no budget side effects — so a
// 2,165-question sweep leaves no footprint. Scoring is citation-level P/R/F1
// via BenchmarkScorer; aggregation is per-question means plus a per-Act
// category breakdown (the dataset's `Act` column is its category axis).
public class BenchmarkRunnerService : IBenchmarkRunner
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    private readonly IBenchmarkLoader _loader;
    private readonly IRagContextBuilder _ragContextBuilder;
    private readonly IPromptAssembler _promptAssembler;
    private readonly MuktoAin.Domain.Interfaces.IAiService _aiService;
    private readonly DisclaimerInjector _disclaimerInjector;

    public BenchmarkRunnerService(
        IBenchmarkLoader loader,
        IRagContextBuilder ragContextBuilder,
        IPromptAssembler promptAssembler,
        MuktoAin.Domain.Interfaces.IAiService aiService,
        DisclaimerInjector disclaimerInjector)
    {
        _loader = loader;
        _ragContextBuilder = ragContextBuilder;
        _promptAssembler = promptAssembler;
        _aiService = aiService;
        _disclaimerInjector = disclaimerInjector;
    }

    internal static string VariantLabel(BenchmarkPromptVariant variant) => variant switch
    {
        BenchmarkPromptVariant.ZeroShot => "zero-shot",
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown benchmark prompt variant.")
    };

    public async Task<BenchmarkRunResultDto> RunAsync(BenchmarkRunOptions options, CancellationToken ct = default)
    {
        options ??= new BenchmarkRunOptions();
        var startedAt = DateTimeOffset.UtcNow;

        var dataset = await _loader.LoadAsync(options.DatasetPath, ct);
        var questions = options.MaxQuestions is > 0
            ? dataset.Questions.Take(options.MaxQuestions.Value).ToList()
            : dataset.Questions.ToList();

        var run = new BenchmarkRunResultDto
        {
            Variant = VariantLabel(options.Variant),
            DatasetPath = dataset.SourcePath,
            TotalQuestions = questions.Count,
            StartedAt = startedAt
        };

        foreach (var question in questions)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                run.Questions.Add(await EvaluateQuestionAsync(question, options, ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One question's retrieval/AI failure must not kill a 2,165-question run.
                run.Questions.Add(new BenchmarkQuestionScoreDto
                {
                    DatasetId = question.DatasetId,
                    Question = question.Question,
                    Language = question.Language,
                    Category = question.Category,
                    Error = ex.Message
                });
            }

            if (options.DelayBetweenQuestionsMs > 0)
            {
                await Task.Delay(options.DelayBetweenQuestionsMs, ct);
            }
        }

        run.ScoredQuestions = run.Questions.Count(q => q.Error is null);
        run.SkippedQuestions = dataset.SkippedRowCount + run.Questions.Count(q => q.Error is not null);
        Aggregate(run);
        run.CompletedAt = DateTimeOffset.UtcNow;

        var outputPath = options.OutputPath ?? BenchmarkDataPathResolver.ResolveResultsOutputPath(
            AppContext.BaseDirectory, $"benchmark/results/{run.Variant}.json");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(run, JsonWriteOptions), ct);
        run.OutputPath = outputPath;

        return run;
    }

    private async Task<BenchmarkQuestionScoreDto> EvaluateQuestionAsync(
        BenchmarkQuestionDto question,
        BenchmarkRunOptions options,
        CancellationToken ct)
    {
        var sections = (await _ragContextBuilder
            .RetrieveContextAsync(question.Question, options.TopK))
            .ToList();

        var prompt = options.Variant switch
        {
            BenchmarkPromptVariant.ZeroShot => await _promptAssembler.AssemblePromptAsync(
                question.Question, sections, question.Language, AiRequestType.RightsExplanation, null, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Variant), options.Variant, "Unknown benchmark prompt variant.")
        };

        var rawResponse = await _aiService.GenerateContentAsync(prompt, ct);
        var finalResponse = _disclaimerInjector.InjectDisclaimer(rawResponse, question.Language);

        var (precision, recall, f1) = BenchmarkScorer.ScoreQuestion(
            question.ExpectedSectionReferences, sections);

        return new BenchmarkQuestionScoreDto
        {
            DatasetId = question.DatasetId,
            Question = question.Question,
            Language = question.Language,
            Category = question.Category,
            Response = finalResponse,
            CitedReferences = sections
                .Select(s => $"{s.ActTitle}, Section {s.SectionNumber}")
                .ToList(),
            ExpectedReferences = question.ExpectedSectionReferences.ToList(),
            Precision = precision,
            Recall = recall,
            F1 = f1
        };
    }

    private static void Aggregate(BenchmarkRunResultDto run)
    {
        var scored = run.Questions.Where(q => q.Error is null).ToList();

        run.MeanPrecision = scored.Count == 0 ? 0 : scored.Average(q => q.Precision);
        run.MeanRecall = scored.Count == 0 ? 0 : scored.Average(q => q.Recall);
        run.MeanF1 = scored.Count == 0 ? 0 : scored.Average(q => q.F1);

        run.Categories = scored
            .GroupBy(q => q.Category)
            .Select(g => new BenchmarkCategoryResultDto
            {
                Category = g.Key,
                QuestionCount = g.Count(),
                MeanPrecision = g.Average(q => q.Precision),
                MeanRecall = g.Average(q => q.Recall),
                MeanF1 = g.Average(q => q.F1)
            })
            .OrderBy(c => c.Category, StringComparer.Ordinal)
            .ToList();
    }
}
```

- [ ] **Step 4: Register the benchmark services in DI**

In `src/MuktoAin.Web/Program.cs`, immediately after the S-2.2 `IAiOrchestrationService` registration block (ends at line 191), insert:

```csharp
// S-3.1 + S-3.2: QA benchmark harness (dataset loader + zero-shot/few-shot
// evaluation runner; S-3.3 reuses the same runner with a prompt-variant option).
builder.Services.AddSingleton<IBenchmarkLoader, BenchmarkLoaderService>();
builder.Services.AddScoped<IBenchmarkRunner, BenchmarkRunnerService>();
```

- [ ] **Step 5: Expose `Program` to `WebApplicationFactory`**

Append at the very end of `src/MuktoAin.Web/Program.cs` (after `app.Run();`):

```csharp
// S-3.2: exposes the top-level-statement Program class to WebApplicationFactory<T>
// in the opt-in QA benchmark integration tests.
public partial class Program { }
```

- [ ] **Step 6: Wire the integration-test project for the opt-in run**

In `tests/MuktoAin.IntegrationTests/MuktoAin.IntegrationTests.csproj`, add to the existing `PackageReference` `ItemGroup`:

```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.11" />
```

and add to the existing `ProjectReference` `ItemGroup`:

```xml
<ProjectReference Include="..\..\src\MuktoAin.Web\MuktoAin.Web.csproj" />
```

- [ ] **Step 7: Create the opt-in integration tests**

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Enums;

namespace MuktoAin.IntegrationTests.AiPipeline;

// S-3.2: opt-in QA benchmark harness over the REAL full pipeline (SQL + Qdrant +
// Gemini) via the Web app's DI container. Normal `dotnet test` runs are a no-op;
// to execute a real run (prerequisites: local MSSQL seeded, Qdrant up, Gemini
// keys in appsettings.Development.json or env — the same prerequisites as
// running src/MuktoAin.Web locally):
//   $env:MUKTOAIN_RUN_QA_BENCHMARK = "1"
//   $env:MUKTOAIN_BENCHMARK_MAX_QUESTIONS = "25"   # optional; unset = full dataset
//   dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~QaBenchmark"
// Results land in data/benchmark/results/zero-shot.json (the zero-shot baseline
// report for the S-3.3 comparison).
public class QaBenchmarkTests
{
    private static bool OptIn() =>
        string.Equals(Environment.GetEnvironmentVariable("MUKTOAIN_RUN_QA_BENCHMARK"), "1", StringComparison.Ordinal);

    private static int? MaxQuestions() =>
        int.TryParse(Environment.GetEnvironmentVariable("MUKTOAIN_BENCHMARK_MAX_QUESTIONS"), out var n) && n > 0
            ? n
            : null;

    [Fact]
    public async Task ZeroShot_Baseline_Run_Writes_Results_Json()
    {
        if (!OptIn()) return; // opt-in harness: skipped silently in normal CI

        await using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IBenchmarkRunner>();

        var result = await runner.RunAsync(new BenchmarkRunOptions
        {
            MaxQuestions = MaxQuestions(),
            DelayBetweenQuestionsMs = 500
        });

        Assert.True(result.TotalQuestions > 0);
        Assert.Equal("zero-shot", result.Variant);
        Assert.True(File.Exists(result.OutputPath));
        Assert.InRange(result.MeanF1, 0.0, 1.0);
        Assert.InRange(result.MeanPrecision, 0.0, 1.0);
        Assert.InRange(result.MeanRecall, 0.0, 1.0);
    }

    [Fact]
    public async Task Loader_Resolves_Committed_Sample_Without_Credentials()
    {
        // Always-on smoke: proves the walk-up path resolver finds the committed
        // sample from the integration-test project's output directory too.
        var loader = new BenchmarkLoaderService();
        var dataset = await loader.LoadAsync();

        Assert.True(dataset.Questions.Count > 0);
        Assert.EndsWith("benchmark-sample.json", dataset.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BenchmarkPromptVariant.ZeroShot, default); // guard: enum exists in Domain
    }
}
```

- [ ] **Step 8: Run the unit and integration tests**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~BenchmarkRunnerServiceTests"`
Expected: PASS (6 tests).

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~QaBenchmark"`
Expected: PASS — the credential-gated test is a silent no-op without `MUKTOAIN_RUN_QA_BENCHMARK=1`; the loader test passes from the committed sample.

- [ ] **Step 9: Record completion in plans/Dependency_plan.md**

Flip line 146 of `plans/Dependency_plan.md` from

```markdown
- [ ] **[S-3.2]** Benchmark Runner (Zero-Shot Baseline Evaluation) — *Arpita* `[Blocked by: S-3.1] [Unblocks: S-3.3]`
```

to

```markdown
~~- [x] **[S-3.2]** Benchmark Runner (Zero-Shot Baseline Evaluation) — *Arpita* `[Blocked by: S-3.1] [Unblocks: S-3.3]`~~
```

---

### Task 5: Few-Shot IRAC Prompt Assembly (`PromptAssembler` extension)

**Files:**
- Modify: `src/MuktoAin.Domain/Constants/PromptTemplates.cs` (append after the `DocumentDrafting` constant)
- Modify: `src/MuktoAin.Domain/Interfaces/Services/IPromptAssembler.cs` (full new content below)
- Modify: `src/MuktoAin.Application/Services/PromptAssembler.cs` (extract shared context builder + add new method; full new content below)
- Test: `tests/MuktoAin.UnitTests/Services/PromptAssemblerTests.cs` (append one test)

**Interfaces:**
- Consumes: existing `PromptTemplates` constants, `RetrievedSection`, `Disclaimers.ForLanguage`, `IScenarioMappingRepository.SearchByKeywordAsync`.
- Produces: `PromptTemplates.RightsExplanationFewShotIrac`, `PromptTemplates.FewShotIracExampleEnglish`, `PromptTemplates.FewShotIracExampleBangla` constants; `IPromptAssembler.AssembleFewShotIracPromptAsync(string problemDescription, IEnumerable<RetrievedSection> sections, string language, CancellationToken ct = default) : Task<string>` — consumed by Task 6's runner variant. Existing `AssemblePromptAsync` signature and output are unchanged (regression-covered by the existing three `PromptAssemblerTests` tests).

- [ ] **Step 1: Write the failing few-shot test**

Append inside the existing `PromptAssemblerTests` class in `tests/MuktoAin.UnitTests/Services/PromptAssemblerTests.cs`:

```csharp
    [Fact]
    public async Task AssembleFewShotIracPromptAsync_IncludesExemplarsIracHeadingsContextAndDisclaimer()
    {
        var sections = new List<RetrievedSection>
        {
            new(123, "Bangladesh Labour Act, 2006", "123", "The wages of every worker shall be paid...", 0.9f, RetrievalMethod.Vector)
        };

        _scenarioRepoMock.Setup(r => r.SearchByKeywordAsync(It.IsAny<string>()))
            .ReturnsAsync(Enumerable.Empty<ScenarioMapping>());

        var prompt = await _assembler.AssembleFewShotIracPromptAsync(
            "My employer has not paid my wages for 3 months",
            sections,
            "en");

        Assert.Contains("My employer has not paid my wages for 3 months", prompt);
        Assert.Contains("IRAC", prompt);
        Assert.Contains("Issue:", prompt);
        Assert.Contains("Rule:", prompt);
        Assert.Contains("Application:", prompt);
        Assert.Contains("Conclusion:", prompt);
        Assert.Contains("Section 123", prompt);
        Assert.Contains("The wages of every worker shall be paid", prompt);
        Assert.Contains("English", prompt);
        Assert.Contains(Disclaimers.Legal, prompt);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PromptAssemblerTests"`
Expected: FAIL — compilation error `AssembleFewShotIracPromptAsync` is not defined on `IPromptAssembler`.

- [ ] **Step 3: Add the few-shot template constants**

In `src/MuktoAin.Domain/Constants/PromptTemplates.cs`, append inside the `PromptTemplates` class (after the `DocumentDrafting` constant):

```csharp
    public const string RightsExplanationFewShotIrac = """
        You are a legal information assistant for Bangladesh.
        A citizen has described this problem: {problem}

        Study these worked examples, each answered with the IRAC structure
        (Issue, Rule, Application, Conclusion), citing only retrieved statutes:

        {examples}

        Now answer the citizen's problem above using the same IRAC structure.
        Based ONLY on the following statutory sections, explain their rights
        in plain {language}. Cite specific Act names and Section numbers.

        Relevant statutory text:
        {context}

        Rules:
        - Only cite sections provided above. Never fabricate citations.
        - Structure the answer with clear Issue, Rule, Application, Conclusion headings.
        - Use simple language a non-lawyer can understand.
        - If the provided sections don't cover the problem, say so explicitly.
        - End with: {disclaimer}
        """;

    // Few-shot exemplars (requirements.md §4: "Representative QA samples with
    // IRAC explanations injected via PromptAssembler to guide Gemini's citations").
    public const string FewShotIracExampleEnglish = """
        Example 1:
        Problem: My employer has not paid my wages for the last three months.
        Relevant statutory text:
        - Bangladesh Labour Act, 2006, Section 123: The wages of every worker shall be paid before the expiry of the seventh working day after the last day of the wage period.
        Answer:
        Issue: Has the employer failed to pay wages within the statutory deadline?
        Rule: Section 123 of the Bangladesh Labour Act, 2006 requires wages to be paid before the expiry of the seventh working day after the last day of the wage period.
        Application: Three months of wages were never paid, so the employer has breached the Section 123 payment deadline.
        Conclusion: You are entitled to the unpaid wages under Section 123 of the Bangladesh Labour Act, 2006.
        """;

    public const string FewShotIracExampleBangla = """
        Example 2:
        Problem: কর্মক্ষেত্রে দুর্ঘটনায় আহত হয়েছি, ক্ষতিপূরণ পাব কি না জানতে চাই।
        Relevant statutory text:
        - Bangladesh Labour Act, 2006, Section 150: If personal injury is caused to a worker by accident arising out of and in the course of his employment, the employer shall be liable to pay compensation.
        Answer:
        Issue: কর্মক্ষেত্রে দুর্ঘটনাজনিত আঘাতের জন্য ক্ষতিপূরণ পাওয়া যাবে কি না?
        Rule: বাংলাদেশ শ্রম আইন, ২০০৬-এর ১৫০ ধারা অনুযায়ী কর্মের সময়ে দুর্ঘটনাজনিত আঘাত হলে নিয়োগকর্তা ক্ষতিপূরণ দিতে বাধ্য।
        Application: আঘাতটি কর্মের সময়ে ও কর্মক্ষেত্রে হয়েছে, তাই ১৫০ ধারার অধীনে ক্ষতিপূরণের দাবি প্রযোজ্য।
        Conclusion: আপনি ১৫০ ধারার অধীনে ক্ষতিপূরণের দাবি করতে পারেন (বাংলাদেশ শ্রম আইন, ২০০৬)।
        """;
```

- [ ] **Step 4: Extend `IPromptAssembler`**

The full new content of `src/MuktoAin.Domain/Interfaces/Services/IPromptAssembler.cs`:

```csharp
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Services;

public interface IPromptAssembler
{
    Task<string> AssemblePromptAsync(
        string problemDescription,
        IEnumerable<RetrievedSection> sections,
        string language,
        AiRequestType requestType,
        string? documentType = null,
        CancellationToken ct = default);

    // S-3.3: few-shot IRAC variant of the rights-explanation prompt
    // (requirements.md §4 "Few-Shot Steering").
    Task<string> AssembleFewShotIracPromptAsync(
        string problemDescription,
        IEnumerable<RetrievedSection> sections,
        string language,
        CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement in `PromptAssembler` (extract shared context builder)**

The full new content of `src/MuktoAin.Application/Services/PromptAssembler.cs`. The context-building block from the old `AssemblePromptAsync` moves verbatim into `BuildContextAsync`; both public methods call it, so existing prompts stay byte-identical:

```csharp
using System.Text;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.Application.Services;

public class PromptAssembler : IPromptAssembler
{
    private readonly IScenarioMappingRepository _scenarioMappingRepo;

    public PromptAssembler(IScenarioMappingRepository scenarioMappingRepo)
    {
        _scenarioMappingRepo = scenarioMappingRepo;
    }

    public async Task<string> AssemblePromptAsync(
        string problemDescription,
        IEnumerable<RetrievedSection> sections,
        string language,
        AiRequestType requestType,
        string? documentType = null,
        CancellationToken ct = default)
    {
        var targetLanguage = string.Equals(language, "bn", StringComparison.OrdinalIgnoreCase) ? "Bengali" : "English";
        var disclaimer = Disclaimers.ForLanguage(language);
        var context = await BuildContextAsync(problemDescription, sections);

        var template = requestType switch
        {
            AiRequestType.Drafting => PromptTemplates.DocumentDrafting,
            _ => PromptTemplates.RightsExplanation,
        };

        var prompt = template
            .Replace("{problem}", problemDescription?.Trim() ?? string.Empty)
            .Replace("{language}", targetLanguage)
            .Replace("{context}", context)
            .Replace("{disclaimer}", disclaimer)
            .Replace("{documentType}", documentType ?? "Legal Document");

        return prompt;
    }

    public async Task<string> AssembleFewShotIracPromptAsync(
        string problemDescription,
        IEnumerable<RetrievedSection> sections,
        string language,
        CancellationToken ct = default)
    {
        var targetLanguage = string.Equals(language, "bn", StringComparison.OrdinalIgnoreCase) ? "Bengali" : "English";
        var disclaimer = Disclaimers.ForLanguage(language);
        var context = await BuildContextAsync(problemDescription, sections);

        var examples = new StringBuilder();
        examples.AppendLine(PromptTemplates.FewShotIracExampleEnglish.TrimEnd());
        examples.AppendLine();
        examples.Append(PromptTemplates.FewShotIracExampleBangla.TrimEnd());

        return PromptTemplates.RightsExplanationFewShotIrac
            .Replace("{problem}", problemDescription?.Trim() ?? string.Empty)
            .Replace("{language}", targetLanguage)
            .Replace("{examples}", examples.ToString())
            .Replace("{context}", context)
            .Replace("{disclaimer}", disclaimer);
    }

    // Shared by both variants: statutory context lines + curated scenario-mapping
    // hints (FR-18), identical to the block that previously lived inline in
    // AssemblePromptAsync.
    private async Task<string> BuildContextAsync(
        string problemDescription,
        IEnumerable<RetrievedSection> sections)
    {
        var contextBuilder = new StringBuilder();
        var sectionList = sections?.ToList() ?? new List<RetrievedSection>();

        if (sectionList.Count > 0)
        {
            foreach (var section in sectionList)
            {
                contextBuilder.AppendLine($"Act: {section.ActTitle}, Section {section.SectionNumber}: {section.SectionText}");
                contextBuilder.AppendLine();
            }
        }
        else
        {
            contextBuilder.AppendLine("No specific statutory sections retrieved for this problem.");
            contextBuilder.AppendLine();
        }

        // Check scenario mappings for keyword boost hints
        if (!string.IsNullOrWhiteSpace(problemDescription))
        {
            try
            {
                var mappings = (await _scenarioMappingRepo.SearchByKeywordAsync(problemDescription)).ToList();
                if (mappings.Count > 0)
                {
                    contextBuilder.AppendLine("Curated Scenario Guidance:");
                    foreach (var m in mappings)
                    {
                        var note = string.IsNullOrWhiteSpace(m.Notes) ? string.Empty : $" ({m.Notes})";
                        contextBuilder.AppendLine($"- Keyword '{m.ScenarioKeyword}' maps to Section ID {m.SectionId}{note}");
                    }
                    contextBuilder.AppendLine();
                }
            }
            catch
            {
                // Graceful degradation: scenario mapping search failure shouldn't block prompt assembly
            }
        }

        return contextBuilder.ToString().TrimEnd();
    }
}
```

- [ ] **Step 6: Run the prompt-assembler tests (new + regression)**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PromptAssemblerTests"`
Expected: PASS — 4 tests (3 existing + 1 new). The 3 existing tests passing proves the `BuildContextAsync` extraction changed nothing.

---

### Task 6: S-3.3 — Few-Shot Variant, Re-Run, and Zero-Shot vs Few-Shot Comparison

**Files:**
- Modify: `src/MuktoAin.Domain/Enums/BenchmarkPromptVariant.cs` (add `FewShotIrac = 1`)
- Modify: `src/MuktoAin.Application/Services/IBenchmarkRunner.cs` (add `CompareAsync`)
- Modify: `src/MuktoAin.Application/Services/BenchmarkRunnerService.cs` (add variant branch + `CompareAsync` implementation)
- Modify: `src/MuktoAin.Application/DTOs/BenchmarkComparisonDto.cs` (create)
- Modify: `tests/MuktoAin.IntegrationTests/AiPipeline/QaBenchmarkTests.cs` (append few-shot + comparison tests)
- Test: `tests/MuktoAin.UnitTests/Services/BenchmarkRunnerServiceTests.cs` (append variant + comparison tests)
- Modify: `plans/Dependency_plan.md:147` (flip S-3.3)

**Files (exact new path):**
- Create: `src/MuktoAin.Application/DTOs/BenchmarkComparisonDto.cs`

**Interfaces:**
- Consumes: `PromptAssembler.AssembleFewShotIracPromptAsync` (Task 5), `BenchmarkRunResultDto` JSON round-trip (Task 1), `BenchmarkDataPathResolver.ResolveResultsOutputPath` (Task 2).
- Produces: `BenchmarkPromptVariant.FewShotIrac = 1`; `IBenchmarkRunner.CompareAsync(string zeroShotResultsPath, string fewShotResultsPath, string? outputPath = null, CancellationToken ct = default) : Task<BenchmarkComparisonDto>`; `BenchmarkComparisonDto` record (`ZeroShotResultsPath, FewShotResultsPath, ZeroShotMeanF1, FewShotMeanF1, MeanF1Delta, QuestionsImproved, QuestionsRegressed, Categories`); `BenchmarkCategoryComparisonDto` record (`Category, ZeroShotMeanF1, FewShotMeanF1, MeanF1Delta`). Outputs `few-shot.json` and `comparison-zero-shot-vs-few-shot.json` under `data/benchmark/results/`.

- [ ] **Step 1: Write the failing variant + comparison tests**

Append inside the existing `BenchmarkRunnerServiceTests` class:

```csharp
    [Fact]
    public async Task RunAsync_FewShotIrac_Uses_The_FewShot_Assembler()
    {
        SetupHappyPath(Question(1, "A"));
        _promptMock
            .Setup(p => p.AssembleFewShotIracPromptAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<RetrievedSection>>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("FEW-SHOT IRAC PROMPT");
        var service = CreateService();

        var result = await service.RunAsync(new BenchmarkRunOptions
        {
            Variant = BenchmarkPromptVariant.FewShotIrac,
            OutputPath = Path.Combine(Path.GetTempPath(), $"bench-{Guid.NewGuid():N}.json")
        });

        Assert.Equal("few-shot-irac", result.Variant);
        _promptMock.Verify(p => p.AssembleFewShotIracPromptAsync(
            It.IsAny<string>(), It.IsAny<IEnumerable<RetrievedSection>>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _promptMock.Verify(p => p.AssemblePromptAsync(
            It.IsAny<string>(), It.IsAny<IEnumerable<RetrievedSection>>(), It.IsAny<string>(),
            It.IsAny<AiRequestType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void VariantLabel_Maps_FewShotIrac()
    {
        Assert.Equal("few-shot-irac", BenchmarkRunnerService.VariantLabel(BenchmarkPromptVariant.FewShotIrac));
    }

    [Fact]
    public async Task CompareAsync_Detects_Improvement_Regression_And_Writes_Report()
    {
        var zeroShot = new BenchmarkRunResultDto
        {
            Variant = "zero-shot",
            MeanF1 = 0.4,
            Questions =
            [
                new() { DatasetId = 1, Category = "Cat A", F1 = 0.6, Error = null },
                new() { DatasetId = 2, Category = "Cat A", F1 = 0.2, Error = null },
                new() { DatasetId = 3, Category = "Cat B", F1 = 0.4, Error = null }
            ]
        };
        var fewShot = new BenchmarkRunResultDto
        {
            Variant = "few-shot-irac",
            MeanF1 = 0.7,
            Questions =
            [
                new() { DatasetId = 1, Category = "Cat A", F1 = 0.8, Error = null },  // improved
                new() { DatasetId = 2, Category = "Cat A", F1 = 0.1, Error = null },  // regressed
                new() { DatasetId = 3, Category = "Cat B", F1 = 0.9, Error = null }   // improved
            ]
        };
        var dir = Path.Combine(Path.GetTempPath(), $"bench-cmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var zeroPath = Path.Combine(dir, "zero.json");
        var fewPath = Path.Combine(dir, "few.json");
        File.WriteAllText(zeroPath, JsonSerializer.Serialize(zeroShot));
        File.WriteAllText(fewPath, JsonSerializer.Serialize(fewShot));
        var service = CreateService();

        var comparison = await service.CompareAsync(zeroPath, fewPath);

        Assert.Equal(zeroPath, comparison.ZeroShotResultsPath);
        Assert.Equal(fewPath, comparison.FewShotResultsPath);
        Assert.Equal(0.4, comparison.ZeroShotMeanF1);
        Assert.Equal(0.7, comparison.FewShotMeanF1);
        Assert.Equal(0.3, comparison.MeanF1Delta, precision: 4);
        Assert.Equal(2, comparison.QuestionsImproved);
        Assert.Equal(1, comparison.QuestionsRegressed);
        Assert.Equal(2, comparison.Categories.Count);
        var catA = comparison.Categories.Single(c => c.Category == "Cat A");
        Assert.Equal(0.4, catA.ZeroShotMeanF1, precision: 4);                      // (0.6+0.2)/2 = 0.4
        Assert.Equal(0.45, catA.FewShotMeanF1, precision: 4);                      // (0.8+0.1)/2 = 0.45
        Assert.True(File.Exists(comparison.OutputPath));
        Directory.Delete(dir, recursive: true);
    }
```

Notes: `BenchmarkComparisonDto` gains an `OutputPath` property (set by `CompareAsync`, see Step 3) so the test can assert the report file was written. The test file must add `using System.Text.Json;` at the top (the file created in Task 4 doesn't have it).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~BenchmarkRunnerServiceTests"`
Expected: FAIL — compilation errors: `BenchmarkPromptVariant.FewShotIrac` not defined, `CompareAsync` not defined, `BenchmarkComparisonDto` not found.

- [ ] **Step 3: Add the enum member, comparison DTOs, and runner implementation**

In `src/MuktoAin.Domain/Enums/BenchmarkPromptVariant.cs`, add the member:

```csharp
namespace MuktoAin.Domain.Enums;

// S-3.2/S-3.3: which prompt-assembly strategy the benchmark runner drives.
public enum BenchmarkPromptVariant
{
    ZeroShot = 0,
    FewShotIrac = 1
}
```

Create `src/MuktoAin.Application/DTOs/BenchmarkComparisonDto.cs`:

```csharp
namespace MuktoAin.Application.DTOs;

// Zero-shot vs few-shot comparison (S-3.3) — serialized to
// data/benchmark/results/comparison-zero-shot-vs-few-shot.json.
public sealed record BenchmarkCategoryComparisonDto(
    string Category,
    double ZeroShotMeanF1,
    double FewShotMeanF1,
    double MeanF1Delta);

public sealed record BenchmarkComparisonDto(
    string ZeroShotResultsPath,
    string FewShotResultsPath,
    double ZeroShotMeanF1,
    double FewShotMeanF1,
    double MeanF1Delta,
    int QuestionsImproved,
    int QuestionsRegressed,
    IReadOnlyList<BenchmarkCategoryComparisonDto> Categories)
{
    // Set by CompareAsync after writing the report file.
    public string OutputPath { get; set; } = string.Empty;
}
```

In `src/MuktoAin.Application/Services/IBenchmarkRunner.cs`, add the method:

```csharp
using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

public interface IBenchmarkRunner
{
    Task<BenchmarkRunResultDto> RunAsync(BenchmarkRunOptions options, CancellationToken ct = default);

    // S-3.3: compares the two variant reports and writes the comparison JSON.
    Task<BenchmarkComparisonDto> CompareAsync(
        string zeroShotResultsPath,
        string fewShotResultsPath,
        string? outputPath = null,
        CancellationToken ct = default);
}
```

In `src/MuktoAin.Application/Services/BenchmarkRunnerService.cs`, make three changes:

1. Add a read-options field next to `JsonWriteOptions`:

```csharp
    private static readonly JsonSerializerOptions JsonReadOptions = new() { PropertyNameCaseInsensitive = true };
```

2. Extend `VariantLabel` and the `EvaluateQuestionAsync` switch:

```csharp
    internal static string VariantLabel(BenchmarkPromptVariant variant) => variant switch
    {
        BenchmarkPromptVariant.ZeroShot => "zero-shot",
        BenchmarkPromptVariant.FewShotIrac => "few-shot-irac",
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown benchmark prompt variant.")
    };
```

```csharp
        var prompt = options.Variant switch
        {
            BenchmarkPromptVariant.ZeroShot => await _promptAssembler.AssemblePromptAsync(
                question.Question, sections, question.Language, AiRequestType.RightsExplanation, null, ct),
            BenchmarkPromptVariant.FewShotIrac => await _promptAssembler.AssembleFewShotIracPromptAsync(
                question.Question, sections, question.Language, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Variant), options.Variant, "Unknown benchmark prompt variant.")
        };
```

3. Append the `CompareAsync` method to the class:

```csharp
    public async Task<BenchmarkComparisonDto> CompareAsync(
        string zeroShotResultsPath,
        string fewShotResultsPath,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var zeroShot = JsonSerializer.Deserialize<BenchmarkRunResultDto>(
            await File.ReadAllTextAsync(zeroShotResultsPath, ct), JsonReadOptions)
            ?? throw new InvalidOperationException($"'{zeroShotResultsPath}' deserialized to no data.");
        var fewShot = JsonSerializer.Deserialize<BenchmarkRunResultDto>(
            await File.ReadAllTextAsync(fewShotResultsPath, ct), JsonReadOptions)
            ?? throw new InvalidOperationException($"'{fewShotResultsPath}' deserialized to no data.");

        var zeroScores = zeroShot.Questions
            .Where(q => q.Error is null)
            .ToDictionary(q => q.DatasetId, q => q.F1);
        var fewScores = fewShot.Questions
            .Where(q => q.Error is null)
            .ToDictionary(q => q.DatasetId, q => q.F1);

        var zeroByCategory = zeroShot.Categories.ToDictionary(c => c.Category, c => c.MeanF1);
        var fewByCategory = fewShot.Categories.ToDictionary(c => c.Category, c => c.MeanF1);
        var categories = zeroByCategory.Keys.Union(fewByCategory.Keys)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k =>
            {
                var zero = zeroByCategory.GetValueOrDefault(k);
                var few = fewByCategory.GetValueOrDefault(k);
                return new BenchmarkCategoryComparisonDto(k, zero, few, few - zero);
            })
            .ToList();

        var comparison = new BenchmarkComparisonDto(
            zeroShotResultsPath,
            fewShotResultsPath,
            zeroShot.MeanF1,
            fewShot.MeanF1,
            fewShot.MeanF1 - zeroShot.MeanF1,
            fewScores.Count(kv => zeroScores.TryGetValue(kv.Key, out var zeroF1) && kv.Value > zeroF1),
            fewScores.Count(kv => zeroScores.TryGetValue(kv.Key, out var zeroF1) && kv.Value < zeroF1),
            categories);

        var resolvedOutputPath = outputPath ?? BenchmarkDataPathResolver.ResolveResultsOutputPath(
            AppContext.BaseDirectory, "benchmark/results/comparison-zero-shot-vs-few-shot.json");
        await File.WriteAllTextAsync(
            resolvedOutputPath, JsonSerializer.Serialize(comparison, JsonWriteOptions), ct);
        comparison.OutputPath = resolvedOutputPath;

        return comparison;
    }
```

- [ ] **Step 4: Run the unit tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~BenchmarkRunnerServiceTests"`
Expected: PASS — 9 tests (6 existing + 3 new).

- [ ] **Step 5: Append the few-shot + comparison integration tests**

Add to the `QaBenchmarkTests` class in `tests/MuktoAin.IntegrationTests/AiPipeline/QaBenchmarkTests.cs`:

```csharp
    [Fact]
    public async Task FewShotIrac_Run_Then_Comparison_Write_Reports()
    {
        if (!OptIn()) return; // opt-in harness: skipped silently in normal CI

        var resultsDir = Path.Combine(Path.GetTempPath(), $"qa-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(resultsDir);
        var zeroPath = Path.Combine(resultsDir, "zero-shot.json");
        var fewPath = Path.Combine(resultsDir, "few-shot.json");

        await using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IBenchmarkRunner>();

        var zeroShot = await runner.RunAsync(new BenchmarkRunOptions
        {
            MaxQuestions = MaxQuestions(),
            DelayBetweenQuestionsMs = 500,
            OutputPath = zeroPath
        });
        var fewShot = await runner.RunAsync(new BenchmarkRunOptions
        {
            Variant = BenchmarkPromptVariant.FewShotIrac,
            MaxQuestions = MaxQuestions(),
            DelayBetweenQuestionsMs = 500,
            OutputPath = fewPath
        });
        var comparison = await runner.CompareAsync(zeroShot.OutputPath, fewShot.OutputPath);

        Assert.Equal("few-shot-irac", fewShot.Variant);
        Assert.True(File.Exists(zeroShot.OutputPath));
        Assert.True(File.Exists(fewShot.OutputPath));
        Assert.True(File.Exists(comparison.OutputPath));
        Assert.InRange(fewShot.MeanF1, 0.0, 1.0);
        Assert.InRange(comparison.MeanF1Delta, -1.0, 1.0);
    }
```

Also update the class doc-comment block at the top of `QaBenchmarkTests.cs` to append:

```csharp
// For the S-3.3 few-shot re-evaluation + comparison:
//   dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~QaBenchmark"
// after the zero-shot baseline has run — the few-shot report lands in
// data/benchmark/results/few-shot.json and the comparison in
// data/benchmark/results/comparison-zero-shot-vs-few-shot.json.
```

- [ ] **Step 6: Run the integration tests (no-op without opt-in)**

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~QaBenchmark"`
Expected: PASS — both credential-gated tests return silently; the loader smoke test passes.

- [ ] **Step 7: Run the full solution build + full test suite**

Run: `dotnet build MuktoAin.sln` then `dotnet test tests/MuktoAin.UnitTests` then `dotnet test tests/MuktoAin.IntegrationTests`
Expected: Build succeeded; all tests PASS.

- [ ] **Step 8: Execute the real few-shot re-evaluation (manual, opt-in — for the academic report)**

With the local stack running (MSSQL seeded, Qdrant up, Gemini keys configured):

```powershell
$env:MUKTOAIN_RUN_QA_BENCHMARK = "1"
dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~QaBenchmark"
```

Expected outputs (committed to the working tree only by Shads — files under `data/benchmark/results/` are untracked artifacts):
- `data/benchmark/results/zero-shot.json` (zero-shot baseline, from Task 4's run)
- `data/benchmark/results/few-shot.json` (few-shot IRAC re-run)
- `data/benchmark/results/comparison-zero-shot-vs-few-shot.json` (per-question improved/regressed counts + per-Act F1 deltas — the S-3.3 deliverable)

- [ ] **Step 9: Record completion in plans/Dependency_plan.md**

Flip line 147 of `plans/Dependency_plan.md` from

```markdown
- [ ] **[S-3.3]** Few-Shot IRAC Prompt Assembly & Re-Evaluation — *Arpita* `[Blocked by: S-3.2]`
```

to

```markdown
~~- [x] **[S-3.3]** Few-Shot IRAC Prompt Assembly & Re-Evaluation — *Arpita* `[Blocked by: S-3.2]`~~
```
