namespace MuktoAin.Application.DTOs;

// T-3.1 read/save models for the /Admin/Acts management surface (E-3.2 wiring).
// Column limits mirror scripts/02_schema.sql (ACT table).

public record ActListDto(
    int ActId,
    string Title,
    string ActNumber,
    int Year,
    string Language,
    bool IsRepealed,
    int SectionCount,
    string SourceUrl,
    DateTime ImportedAt);

public record ActPageDto(
    IReadOnlyList<ActListDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public record ActSectionDto(
    int SectionId,
    int OrdinalPosition,
    string? SectionNumber,
    string? SectionTitle,
    string SectionText,
    int ChunkCount,
    int StaleChunkCount);

public record ActDetailDto(
    int ActId,
    string Title,
    string ActNumber,
    int Year,
    string PublicationDate,
    string Language,
    bool IsRepealed,
    int TokenCount,
    string SourceUrl,
    DateTime ImportedAt,
    IReadOnlyList<ActSectionDto> Sections);

public record ActSaveDto(
    string Title,
    string ActNumber,
    int Year,
    string PublicationDate,
    string Language,
    bool IsRepealed,
    string SourceUrl);

public record ActSaveResultDto(bool Success, string? Error, int? ActId)
{
    public static ActSaveResultDto Ok(int actId) => new(true, null, actId);
    public static ActSaveResultDto Fail(string error) => new(false, error, null);
}

public record ActDeleteResultDto(bool Success, string? Error)
{
    public static ActDeleteResultDto Ok() => new(true, null);
    public static ActDeleteResultDto Fail(string error) => new(false, error);
}

// FR-17: result of the SHA-256 content-hash integrity scan for one act.
// Fresh = stored hash matches recomputed; Stale = mismatch (re-embed queued via
// MarkStaleAsync); Pending = never stamped by EmbeddingBatchJob (ContentHash null).
public record ActReindexResultDto(
    int ActId,
    int TotalChunks,
    int FreshChunks,
    int StaleChunks,
    int PendingChunks);
