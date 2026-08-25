namespace MuktoAin.Domain.Entities;

public class Act
{
    public int ActId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ActNumber { get; set; } = string.Empty;
    public int Year { get; set; }

    // Raw text, not DateTime -- pre-1900s calendar variability (design.md 2.3)
    public string PublicationDate { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;
    public bool IsRepealed { get; set; }
    public int TokenCount { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }

    public ICollection<ActSection> Sections { get; set; } = new List<ActSection>();
    public ICollection<ActFootnote> Footnotes { get; set; } = new List<ActFootnote>();
}
