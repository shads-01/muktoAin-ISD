namespace MuktoAin.Domain.Entities;

public class CaseCategory
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string NameBn { get; set; } = string.Empty;
    public string DescriptionBn { get; set; } = string.Empty;

    // Pipe-delimited list of commonly-filed complaint types for this category
    // (shown on /Category/Details). Stored as delimited text rather than a child
    // table -- it's short, ordered, display-only copy with no independent identity
    // of its own (never queried, referenced, or edited row-by-row), same tier as
    // Name/Description. Split on '|' at the presentation edge (CategoryController).
    public string CommonActions { get; set; } = string.Empty;

    // English counterpart of CommonActions, same pipe-delimited/index-aligned
    // shape. Kept as a separate column (not a translation table) for the same
    // reason as CommonActions itself -- display-only copy, no independent identity.
    public string CommonActionsEn { get; set; } = string.Empty;

    public ICollection<Case> Cases { get; set; } = new List<Case>();
}
