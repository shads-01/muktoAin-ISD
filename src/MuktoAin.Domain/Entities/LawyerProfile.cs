using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

// Table-per-Type specialization of User. Contains NO direct CaseId -- reviews reach
// cases solely via GeneratedDocument (design.md 2.1).
public class LawyerProfile
{
    public int LawyerProfileId { get; set; }

    // 1-to-(0..1) relationship: UNIQUE FK, not a shared PK.
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string BarRegistrationNumber { get; set; } = string.Empty;
    public VerificationStatus VerificationStatus { get; set; }

    // Admin who audited the credentials
    public int? VerifiedByAdminId { get; set; }
    public User? VerifiedByAdmin { get; set; }

    public string? Specialization { get; set; }
    public DateTime? VerifiedAt { get; set; }

    public ICollection<LawyerReview> Reviews { get; set; } = new List<LawyerReview>();
}
