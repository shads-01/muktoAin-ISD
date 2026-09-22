namespace MuktoAin.Domain.Entities;

// Lawyer payout request ("পরিশোধ চান") — approved by admin (sandbox marks paid).
public class PayoutRequest
{
    public int PayoutRequestId { get; set; }

    public int LawyerProfileId { get; set; }
    public LawyerProfile LawyerProfile { get; set; } = null!;

    public decimal Amount { get; set; }
    public bool IsPaid { get; set; }

    public DateTime RequestedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
