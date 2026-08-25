using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

public class CaseActReference
{
    public int CaseActReferenceId { get; set; }
    public int CaseId { get; set; }
    public Case Case { get; set; } = null!;

    // Section-level citation
    public int SectionId { get; set; }
    public ActSection Section { get; set; } = null!;

    public decimal RelevanceScore { get; set; }
    public RetrievalMethod RetrievalMethod { get; set; }
}
