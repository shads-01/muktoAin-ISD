namespace MuktoAin.Domain.Entities;

public class ScenarioMapping
{
    public int MappingId { get; set; }
    public int SectionId { get; set; }
    public ActSection Section { get; set; } = null!;

    // Hand-curated trigger keyword
    public string ScenarioKeyword { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
