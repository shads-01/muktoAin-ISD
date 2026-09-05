using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

// Sandbox payment (FR-24). Honorarium orders carry the lawyer split on the
// order row itself (gross / commission / net — single-table ledger).
public class PaymentOrder
{
    public int PaymentOrderId { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    // The case the honorarium belongs to (null for TopUp)
    public int? CaseId { get; set; }
    public Case? Case { get; set; }

    // The lawyer receiving the net (null for TopUp)
    public int? LawyerProfileId { get; set; }
    public LawyerProfile? LawyerProfile { get; set; }

    public PaymentPurpose Purpose { get; set; }
    public PaymentStatus Status { get; set; }

    // All amounts in BDT
    public decimal Amount { get; set; }
    public decimal Commission { get; set; }
    public decimal NetToLawyer { get; set; }

    public string? GatewayRef { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? RefundedAt { get; set; }
}
