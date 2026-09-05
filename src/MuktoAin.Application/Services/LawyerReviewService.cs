using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// FR-13/14/23: specialization-pool queue with claim-based optimistic lock
// (one active review per lawyer; opening a doc claims it), decisions with
// mandatory comments. Rejection reason flows to the citizen's case page and
// (via the chat return link) into the salvage conversation.
public class LawyerReviewService
{
    private readonly IRepository<GeneratedDocument> _docRepo;
    private readonly IRepository<LawyerReview> _reviewRepo;
    private readonly IRepository<LawyerProfile> _profileRepo;
    private readonly ICaseRepository _caseRepo;
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IRepository<District> _districtRepo;
    private readonly IRepository<CaseActReference> _refRepo;
    private readonly IRepository<ActSection> _sectionRepo;
    private readonly IRepository<Act> _actRepo;
    private readonly IEncryptionService _encryptionService;
    private readonly CaseService _caseService;

    public LawyerReviewService(
        IRepository<GeneratedDocument> docRepo,
        IRepository<LawyerReview> reviewRepo,
        IRepository<LawyerProfile> profileRepo,
        ICaseRepository caseRepo,
        IRepository<CaseCategory> categoryRepo,
        IRepository<District> districtRepo,
        IRepository<CaseActReference> refRepo,
        IRepository<ActSection> sectionRepo,
        IRepository<Act> actRepo,
        IEncryptionService encryptionService,
        CaseService caseService)
    {
        _docRepo = docRepo;
        _reviewRepo = reviewRepo;
        _profileRepo = profileRepo;
        _caseRepo = caseRepo;
        _categoryRepo = categoryRepo;
        _districtRepo = districtRepo;
        _refRepo = refRepo;
        _sectionRepo = sectionRepo;
        _actRepo = actRepo;
        _encryptionService = encryptionService;
        _caseService = caseService;
    }

    // Queue = documents in UnderReview, oldest-first (SLA age shown by the view).
    // filter: "All" (default) | "Unclaimed" | "Mine". CanOpen allows re-entry
    // into the lawyer's OWN claimed doc (ClaimAsync auto-allows same lawyer).
    public async Task<IReadOnlyList<QueueItemDto>> GetQueueAsync(
        int? lawyerProfileId = null, string? filter = "All")
    {
        var docs = (await _docRepo.GetAllAsync())
            .Where(d => d.Status == DocumentStatus.UnderReview)
            .AsEnumerable();

        if (filter == "Unclaimed")
            docs = docs.Where(d => !d.AssignedLawyerProfileId.HasValue);
        else if (filter == "Mine" && lawyerProfileId.HasValue)
            docs = docs.Where(d => d.AssignedLawyerProfileId == lawyerProfileId.Value);

        var result = new List<QueueItemDto>();
        foreach (var d in docs.OrderBy(d => d.CreatedAt))
        {
            var c = await _caseRepo.GetWithDocumentsAsync(d.CaseId);
            if (c == null) continue;
            var category = await _categoryRepo.GetByIdAsync(c.CategoryId);
            var district = await _districtRepo.GetByIdAsync(c.DistrictId);
            string? claimedBy = null;
            if (d.AssignedLawyerProfileId.HasValue)
            {
                var p = await _profileRepo.GetByIdAsync(d.AssignedLawyerProfileId.Value);
                claimedBy = p?.BarRegistrationNumber; // admin-safe identifier
            }
            result.Add(new QueueItemDto(
                d.DocumentId,
                d.CaseId,
                SafeDecrypt(c.Title),
                category?.Name ?? "",
                district?.Name ?? "",
                d.Status,
                d.CitizenEdited,
                d.VersionNo,
                claimedBy,
                d.CreatedAt,
                d.ClaimedAt,
                CanOpen: !d.AssignedLawyerProfileId.HasValue
                      || d.AssignedLawyerProfileId == lawyerProfileId));
        }
        return result;
    }

    // Claim = optimistic lock. Returns false if another lawyer already holds it.
    public async Task<bool> ClaimAsync(int documentId, int lawyerProfileId)
    {
        var d = await _docRepo.GetByIdAsync(documentId);
        if (d == null || d.Status != DocumentStatus.UnderReview) return false;
        if (d.AssignedLawyerProfileId.HasValue
            && d.AssignedLawyerProfileId != lawyerProfileId) return false;

        d.AssignedLawyerProfileId = lawyerProfileId;
        d.ClaimedAt = DateTime.UtcNow;
        await _docRepo.SaveChangesAsync();
        return true;
    }

    public async Task<ReviewWorkspaceDto?> GetForReviewAsync(int documentId)
    {
        var d = await _docRepo.GetByIdAsync(documentId);
        if (d == null) return null;
        var c = await _caseRepo.GetWithDocumentsAsync(d.CaseId);
        if (c == null) return null;

        var category = await _categoryRepo.GetByIdAsync(c.CategoryId);
        var district = await _districtRepo.GetByIdAsync(c.DistrictId);

        var citations = new List<CitedSectionDto>();
        var refs = (await _refRepo.GetAllAsync()).Where(r => r.CaseId == d.CaseId);
        foreach (var r in refs)
        {
            var s = await _sectionRepo.GetByIdAsync(r.SectionId);
            var a = s != null ? await _actRepo.GetByIdAsync(s.ActId) : null;
            citations.Add(new CitedSectionDto(
                r.SectionId,
                a?.Title ?? "",
                s?.SectionNumber ?? "",
                s?.SectionText ?? "",
                (float)r.RelevanceScore,
                r.RetrievalMethod.ToString(),
                a?.ActNumber ?? "",
                a?.Year ?? 0));
        }

        return new ReviewWorkspaceDto(
            d.DocumentId,
            d.CaseId,
            SafeDecrypt(c.Title),
            category?.Name ?? "",
            district?.Name ?? "",
            SafeDecrypt(c.Description),
            citations,
            d.ContentDraft,
            d.CitizenEdited ? d.ContentFinal : null,
            d.VersionNo,
            d.CitizenEdited);
    }

    public async Task<bool> SubmitReviewAsync(SubmitReviewDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comments)) return false; // mandatory
        if (dto.Decision == ReviewDecision.EditedApproved
            && string.IsNullOrWhiteSpace(dto.EditedContent)) return false;

        var d = await _docRepo.GetByIdAsync(dto.DocumentId);
        if (d == null || d.Status != DocumentStatus.UnderReview) return false;
        if (d.AssignedLawyerProfileId.HasValue
            && d.AssignedLawyerProfileId != dto.LawyerProfileId) return false;
        // Auto-claim if somehow unclaimed (defensive)
        d.AssignedLawyerProfileId = dto.LawyerProfileId;

        var review = new LawyerReview
        {
            DocumentId = dto.DocumentId,
            LawyerProfileId = dto.LawyerProfileId,
            Decision = dto.Decision,
            Comments = dto.Comments,
            ReviewedAt = DateTime.UtcNow
        };
        await _reviewRepo.AddAsync(review);
        await _reviewRepo.SaveChangesAsync();

        switch (dto.Decision)
        {
            case ReviewDecision.Approved:
                await _documentUpdateAsync(d, DocumentStatus.Approved, null);
                await _caseService.TransitionStatusAsync(d.CaseId, CaseStatus.Finalized);
                break;
            case ReviewDecision.EditedApproved:
                await _documentUpdateAsync(d, DocumentStatus.Approved, dto.EditedContent);
                await _caseService.TransitionStatusAsync(d.CaseId, CaseStatus.Finalized);
                break;
            case ReviewDecision.Rejected:
                await _documentUpdateAsync(d, DocumentStatus.Rejected, null);
                await _caseService.TransitionStatusAsync(d.CaseId, CaseStatus.UnderReview);
                // UnderReview + Rejected document = citizen edit & resubmit loop.
                break;
        }

        // Flag unread activity for the citizen (unread dot on My Cases)
        var c2 = await _caseRepo.GetByIdAsync(d.CaseId);
        if (c2 != null)
        {
            c2.HasUnreadActivity = true;
            await _caseRepo.SaveChangesAsync();
        }
        return true;
    }

    private async Task _documentUpdateAsync(GeneratedDocument d, DocumentStatus status, string? edited)
    {
        // Mirrors DocumentService.UpdateStatusAsync semantics (verified):
        // EditedApproved -> ContentFinal = edited; Approved -> final = draft.
        d.Status = status;
        if (edited != null)
            d.ContentFinal = edited;
        else if (status == DocumentStatus.Approved)
            d.ContentFinal = d.ContentDraft;
        await _docRepo.SaveChangesAsync();
    }

    private string SafeDecrypt(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        try { return _encryptionService.Decrypt(value); }
        catch { return value; }
    }
}
