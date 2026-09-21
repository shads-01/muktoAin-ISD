namespace MuktoAin.Domain.Entities;

// AUD-7: real administrative audit trail (docs/PROJECT_AUDIT_REPORT.md — Admin
// Scope #5). The Admin dashboard "Audit Logs" panel was populated from AI_LOG
// rows (AI calls), not admin actions; nothing recorded WHO suspended a user,
// verified a lawyer, refunded a payment, or deleted a scenario mapping.
// Append-only by design — no code ever updates or deletes these rows.
public class AdminAuditLog
{
    public int AdminAuditLogId { get; set; }

    // The acting admin's [dbo].[USER].UserId (FK)
    public int AdminUserId { get; set; }
    public User? AdminUser { get; set; }

    // Machine-readable action name, e.g. "SuspendUser", "ApproveLawyerVerification",
    // "RefundOrder", "MarkOrderPaid", "DeleteScenario"
    public string Action { get; set; } = string.Empty;

    // Optional: the user the action was performed ON (suspensions, verifications)
    public int? TargetUserId { get; set; }

    // Optional: PK of the affected entity (payment order, lawyer profile, scenario mapping)
    public int? TargetEntityId { get; set; }

    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; }
}
