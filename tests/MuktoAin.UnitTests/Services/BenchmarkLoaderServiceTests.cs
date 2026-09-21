using System.Text.Json;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using Xunit;

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
