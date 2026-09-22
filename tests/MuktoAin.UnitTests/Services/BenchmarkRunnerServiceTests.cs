using System.Text.Json;
using Moq;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using Xunit;

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
}
