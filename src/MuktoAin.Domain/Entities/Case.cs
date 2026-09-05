using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

public class Case
{
    public int CaseId { get; set; }

    // Nullable: supports guest submissions
    public int? UserId { get; set; }
    public User? User { get; set; }

    public int CategoryId { get; set; }
    public CaseCategory Category { get; set; } = null!;

    // Validates geographical context for police station/court jurisdiction
    public byte DistrictId { get; set; }
    public District District { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public CaseStatus Status { get; set; }
    public bool IsAnonymous { get; set; }

    // Guest tracking code for FR-8 (see data pipeline plan Step 1.6; wired by Step 2.1)
    public string? AnonymousTrackingCode { get; set; }

    // Redesign 2026-09: optional notification email for anonymous cases
    // ("no account created" — used ONLY for status-change emails, FR-22)
    public string? NotificationEmail { get; set; }

    // Redesign 2026-09: set when a lawyer acts (claim/decision); cleared
    // when the citizen opens the case page (unread dot on My Cases)
    public bool HasUnreadActivity { get; set; }

    // Redesign 2026-09 (FR-24): honorarium paid marker for approved case
    public bool HonorariumPaid { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<GeneratedDocument> Documents { get; set; } = new List<GeneratedDocument>();
    public ICollection<CaseActReference> ActReferences { get; set; } = new List<CaseActReference>();
}
