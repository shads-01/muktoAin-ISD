using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// Tells lawyers when a citizen sends a document to the review pool (FR-13).
//  - Fresh document: verified lawyers whose free-text Specialization matches
//    the case category by keyword. If nobody matches (or nobody has filled in
//    a specialisation), every verified lawyer is told, so a case in any of
//    the four categories never sits in the queue unnoticed.
//  - Resubmission of a document a lawyer already holds (claimed, then
//    rejected): only that lawyer -- nobody else can open it anyway.
public class LawyerQueueNotifier
{
    // Case category ids (CASE_CATEGORY seed; same ids as ChatService.MapCategory)
    // -> lowercase keywords looked for in LAWYER_PROFILE.Specialization.
    private static readonly Dictionary<int, string[]> CategoryKeywords = new()
    {
        [1] = new[] { "labour", "labor", "employment", "employee", "worker", "workplace", "wage", "industrial",
                      "শ্রম", "চাকরি", "কর্মসংস্থান" },
        [2] = new[] { "criminal", "police", "general diary", "penal", "crime",
                      "ফৌজদারি", "পুলিশ", "অপরাধ", "সাধারণ ডায়েরি" },
        [3] = new[] { "rti", "right to information", "information", "administrative",
                      "তথ্য", "প্রশাসনিক" },
        [4] = new[] { "consumer", "product", "commercial", "trade",
                      "ভোক্তা", "বাণিজ্য" },
    };

    // A general practitioner handles every category.
    private static readonly string[] GeneralKeywords = { "general law", "general practice", "সাধারণ আইন" };

    private readonly IRepository<LawyerProfile> _profileRepo;
    private readonly IRepository<Notification> _notificationRepo;

    public LawyerQueueNotifier(IRepository<LawyerProfile> profileRepo, IRepository<Notification> notificationRepo)
    {
        _profileRepo = profileRepo;
        _notificationRepo = notificationRepo;
    }

    public static bool MatchesCategory(string? specialization, int categoryId)
    {
        if (string.IsNullOrWhiteSpace(specialization)) return false;
        var s = specialization.ToLowerInvariant();
        if (GeneralKeywords.Any(s.Contains)) return true;
        return CategoryKeywords.TryGetValue(categoryId, out var keywords) && keywords.Any(s.Contains);
    }

    // True when the specialization matches at least one case category, i.e.
    // it is usable for narrowing the review queue to the lawyer's field.
    public static bool MatchesAnyCategory(string? specialization) =>
        CategoryKeywords.Keys.Any(id => MatchesCategory(specialization, id));

    public async Task NotifyDocumentQueuedAsync(
        int caseId, int documentId, int categoryId, int? assignedLawyerProfileId)
    {
        try
        {
            var profiles = (await _profileRepo.GetAllAsync()).ToList();
            List<LawyerProfile> recipients;
            NotificationType type;

            var holder = assignedLawyerProfileId.HasValue
                ? profiles.FirstOrDefault(p => p.LawyerProfileId == assignedLawyerProfileId.Value)
                : null;
            if (holder != null)
            {
                recipients = new List<LawyerProfile> { holder };
                type = NotificationType.DocumentResubmitted;
            }
            else
            {
                var verified = profiles.Where(p => p.VerificationStatus == VerificationStatus.Approved).ToList();
                var matching = verified.Where(p => MatchesCategory(p.Specialization, categoryId)).ToList();
                recipients = matching.Count > 0 ? matching : verified;
                type = NotificationType.NewCaseInQueue;
            }

            foreach (var p in recipients)
            {
                await _notificationRepo.AddAsync(new Notification
                {
                    UserId = p.UserId,
                    Type = type,
                    RelatedCaseId = caseId,
                    RelatedDocumentId = documentId,
                    RelatedLawyerProfileId = p.LawyerProfileId,
                    CreatedAt = DateTime.UtcNow
                });
            }
            if (recipients.Count > 0) await _notificationRepo.SaveChangesAsync();
        }
        catch
        {
            // A notification-write failure must not fail sending the document to review.
        }
    }
}
