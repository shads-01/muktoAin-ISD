using MuktoAin.Application.Services;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Models;
using Xunit;

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
        Assert.Equal("thecodeofcivilprocedure1908",
            BenchmarkScorer.Normalize("The Code of Civil Procedure, 1908"));
        Assert.Equal("wagesundersection123", BenchmarkScorer.Normalize("Wages under Section 123!"));
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
