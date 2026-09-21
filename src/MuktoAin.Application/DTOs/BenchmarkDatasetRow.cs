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
