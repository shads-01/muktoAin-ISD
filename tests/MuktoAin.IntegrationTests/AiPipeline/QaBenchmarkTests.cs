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
//
// For the S-3.3 few-shot re-evaluation + comparison:
//   dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~QaBenchmark"
// after the zero-shot baseline has run — the few-shot report lands in
// data/benchmark/results/few-shot.json and the comparison in
// data/benchmark/results/comparison-zero-shot-vs-few-shot.json.
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
}
