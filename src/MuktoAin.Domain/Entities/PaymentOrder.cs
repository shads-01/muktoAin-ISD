using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

// Payment order (FR-24), confirmed through IPaymentGatewayClient. Honorarium orders carry the lawyer split on the
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

    // TopUp only: chat credits this order is worth (scripts/20_ai_chat_credits.sql).
    // Counted into the balance while Paid or Refunded; a refund lowers it by
    // the credits still unused.
    public int ChatCredits { get; set; }

    // Our tran_id, sent to the gateway at session init; the gateway's
    // validation response must echo it back before the order can be Paid.
    public string? TransactionId { get; set; }

    // Gateway the order was sent to (scripts/19_payment_gateway_routing.sql).
    public PaymentGateway Gateway { get; set; }

    // Gateway's own checkout id (bKash paymentID); null for SSLCommerz and the
    // simulator, whose callbacks carry our tran_id.
    public string? GatewaySessionId { get; set; }

    // Gateway's bank transaction id, set on confirmation.
    public string? GatewayRef { get; set; }

    // SQL Server rowversion (scripts/18_payment_gateway.sql): two racing
    // confirmations of one order cannot both mark it Paid.
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? RefundedAt { get; set; }
}
