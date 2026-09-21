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
