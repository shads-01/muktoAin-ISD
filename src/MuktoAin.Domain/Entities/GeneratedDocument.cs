using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

public class GeneratedDocument
{
    public int DocumentId { get; set; }
    public int CaseId { get; set; }
    public Case Case { get; set; } = null!;

    public DocumentType DocumentType { get; set; }

    // Immutable original AI draft
    public string ContentDraft { get; set; } = string.Empty;

    // Lawyer-reviewed/edited content
    public string? ContentFinal { get; set; }

    public DocumentStatus Status { get; set; }
    public string? PdfPath { get; set; }

    // Review-claim guard (see data pipeline plan Step 1.6; wired by Step 2.7)
    public int? AssignedLawyerProfileId { get; set; }
    public LawyerProfile? AssignedLawyerProfile { get; set; }

    // Redesign 2026-09 (FR-21): draft version chain. 1 = immutable AI original;
    // citizen saves increment. Latest citizen edit lives in ContentFinal.
    public int VersionNo { get; set; } = 1;

    // Redesign 2026-09 (FR-23): when this document was claimed from the pool
    public DateTime? ClaimedAt { get; set; }

    // Redesign 2026-09 (FR-21): current citizen edit differs from the AI original
    public bool CitizenEdited { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<LawyerReview> Reviews { get; set; } = new List<LawyerReview>();
}
