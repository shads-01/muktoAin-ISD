namespace MuktoAin.Domain.Entities;

public class ActSection
{
    public int SectionId { get; set; }
    public int ActId { get; set; }
    public Act Act { get; set; } = null!;

    // Guaranteed sorting order within Act
    public int OrdinalPosition { get; set; }

    // Nullable for chapter/part headers
    public string? SectionNumber { get; set; }
    public string? SectionTitle { get; set; }

    // Authoritative statutory text
    public string SectionText { get; set; } = string.Empty;

    public ICollection<ActSectionChunk> Chunks { get; set; } = new List<ActSectionChunk>();
    public ICollection<ScenarioMapping> ScenarioMappings { get; set; } = new List<ScenarioMapping>();
    public ICollection<CaseActReference> CaseReferences { get; set; } = new List<CaseActReference>();
}
