using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

public class LawyerReview
{
    public int ReviewId { get; set; }

    // Reaches Case solely through Document (design.md 2.5)
    public int DocumentId { get; set; }
    public GeneratedDocument Document { get; set; } = null!;

    public int LawyerProfileId { get; set; }
    public LawyerProfile LawyerProfile { get; set; } = null!;

    public ReviewDecision Decision { get; set; }
    public string Comments { get; set; } = string.Empty;
    public DateTime ReviewedAt { get; set; }
}
