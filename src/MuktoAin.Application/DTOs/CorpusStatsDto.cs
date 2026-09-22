namespace MuktoAin.Application.DTOs;

public class CorpusStatsDto
{
    public int TotalSections { get; set; }
    public int TotalChunks { get; set; }
    public int EmbeddedChunks { get; set; }
    public List<ActCorpusStatDto> TopActs { get; set; } = new();
}

public class ActCorpusStatDto
{
    public int ActId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ActNumber { get; set; }
    public int Year { get; set; }
    public string Language { get; set; } = string.Empty;
    public bool IsRepealed { get; set; }
    public int SectionCount { get; set; }
    public int ChunkCount { get; set; }
    public int EmbeddedCount { get; set; }
    public DateTime ImportedAt { get; set; }
}
