using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

// One row per (user, event). Carries only ids + type — bilingual text is
// rendered at read time by NotificationTextFormatter, never stored, so a
// future new UI language needs no data migration.
public class Notification
{
    public int NotificationId { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public NotificationType Type { get; set; }

    public int? RelatedCaseId { get; set; }
    public Case? RelatedCase { get; set; }

    public int? RelatedDocumentId { get; set; }
    public GeneratedDocument? RelatedDocument { get; set; }

    public int? RelatedLawyerProfileId { get; set; }
    public LawyerProfile? RelatedLawyerProfile { get; set; }

    // Read = the user opened this notification's target (clears per-item
    // unread styling and the unread-activity dot on My Cases).
    public bool IsRead { get; set; }

    // Seen = the user opened the bell dropdown while it was listed. Only
    // drives the bell's count badge; never implies IsRead.
    public bool IsSeen { get; set; }

    public DateTime CreatedAt { get; set; }
}
