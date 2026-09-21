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
    private static readonly JsonSerializerOptions JsonReadOptions = new() { PropertyNameCaseInsensitive = true };

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

    public static string VariantLabel(BenchmarkPromptVariant variant) => variant switch
    {
        BenchmarkPromptVariant.ZeroShot => "zero-shot",
        BenchmarkPromptVariant.FewShotIrac => "few-shot-irac",
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
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
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
            BenchmarkPromptVariant.FewShotIrac => await _promptAssembler.AssembleFewShotIracPromptAsync(
                question.Question, sections, question.Language, ct),
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

        var zeroByCategory = zeroShot.Categories.Count > 0
            ? zeroShot.Categories.ToDictionary(c => c.Category, c => c.MeanF1)
            : zeroShot.Questions
                .Where(q => q.Error is null)
                .GroupBy(q => q.Category)
                .ToDictionary(g => g.Key, g => g.Average(q => q.F1));

        var fewByCategory = fewShot.Categories.Count > 0
            ? fewShot.Categories.ToDictionary(c => c.Category, c => c.MeanF1)
            : fewShot.Questions
                .Where(q => q.Error is null)
                .GroupBy(q => q.Category)
                .ToDictionary(g => g.Key, g => g.Average(q => q.F1));
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
        var dir = Path.GetDirectoryName(resolvedOutputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await File.WriteAllTextAsync(
            resolvedOutputPath, JsonSerializer.Serialize(comparison, JsonWriteOptions), ct);
        comparison.OutputPath = resolvedOutputPath;

        return comparison;
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
