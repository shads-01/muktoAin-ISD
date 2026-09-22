using System.Text.RegularExpressions;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Common;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// FR-13/14/23: shared review pool (with an optional "My field" filter by
// specialization) and a claim-based optimistic lock
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
    private readonly IRepository<Notification> _notificationRepo;

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
        CaseService caseService,
        IRepository<Notification> notificationRepo)
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
        _notificationRepo = notificationRepo;
    }

    // Queue = documents in UnderReview, oldest-first (SLA age shown by the view).
    // filter: "All" (default) | "Unclaimed" | "Mine" | "MyField". CanOpen
    // allows re-entry into the lawyer's OWN claimed doc (ClaimAsync
    // auto-allows same lawyer).
    // MyField: documents the lawyer can open whose case category matches their
    // Specialization (same keyword rule as LawyerQueueNotifier). A blank or
    // unmatched specialization falls back to the full pool with FieldFallback set.
    // AUD-8: paged — the page slice is taken BEFORE the expensive per-document
    // enrichment loop so a large backlog enriches only the visible page.
    public async Task<QueuePageDto> GetQueueAsync(
        int? lawyerProfileId = null, string? filter = "All", int page = 1, int pageSize = 20)
    {
        var docs = (await _docRepo.GetAllAsync())
            .Where(d => d.Status == DocumentStatus.UnderReview)
            .AsEnumerable();
        var fieldFallback = false;

        if (filter == "Unclaimed")
            docs = docs.Where(d => !d.AssignedLawyerProfileId.HasValue);
        else if (filter == "Mine" && lawyerProfileId.HasValue)
            docs = docs.Where(d => d.AssignedLawyerProfileId == lawyerProfileId.Value);
        else if (filter == "MyField" && lawyerProfileId.HasValue)
        {
            var profile = await _profileRepo.GetByIdAsync(lawyerProfileId.Value);
            var specialization = profile?.Specialization;
            if (LawyerQueueNotifier.MatchesAnyCategory(specialization))
            {
                var categoryByCase = (await _caseRepo.GetAllAsync())
                    .ToDictionary(c => c.CaseId, c => c.CategoryId);
                docs = docs.Where(d =>
                    (!d.AssignedLawyerProfileId.HasValue || d.AssignedLawyerProfileId == lawyerProfileId.Value)
                    && categoryByCase.TryGetValue(d.CaseId, out var categoryId)
                    && LawyerQueueNotifier.MatchesCategory(specialization, categoryId));
            }
            else
            {
                fieldFallback = true;
            }
        }

        var ordered = docs.OrderBy(d => d.CreatedAt).ToList();
        var totalCount = ordered.Count;

        var result = new List<QueueItemDto>();
        foreach (var d in ordered.Skip((page - 1) * pageSize).Take(pageSize))
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
        return new QueuePageDto(totalCount, result, fieldFallback);
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
        try
        {
            await _docRepo.SaveChangesAsync();
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false; // another lawyer claimed it first (AUD-4)
        }
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

        switch (dto.Decision)
        {
            case ReviewDecision.Approved:
                ApplyDocumentDecision(d, DocumentStatus.Approved, null);
                await _caseService.ApplyStatusTransitionAsync(d.CaseId, CaseStatus.Finalized);
                break;
            case ReviewDecision.EditedApproved:
                ApplyDocumentDecision(d, DocumentStatus.Approved, dto.EditedContent);
                await _caseService.ApplyStatusTransitionAsync(d.CaseId, CaseStatus.Finalized);
                break;
            case ReviewDecision.Rejected:
                ApplyDocumentDecision(d, DocumentStatus.Rejected, null);
                await _caseService.ApplyStatusTransitionAsync(d.CaseId, CaseStatus.UnderReview);
                // UnderReview + Rejected document = citizen edit & resubmit loop.
                break;
        }

        try
        {
            // Review row, claim, document decision and case status all share
            // the request's DbContext, so this one save commits them together:
            // a rowversion conflict on the document or case persists none (AUD-4).
            await _reviewRepo.SaveChangesAsync();
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }

        var c2 = await _caseRepo.GetByIdAsync(d.CaseId);
        if (c2 is { UserId: not null, IsAnonymous: false })
        {
            try
            {
                await _notificationRepo.AddAsync(new Notification
                {
                    UserId = c2.UserId.Value,
                    Type = NotificationType.DocumentDecided,
                    RelatedCaseId = c2.CaseId,
                    RelatedDocumentId = d.DocumentId,
                    CreatedAt = DateTime.UtcNow
                });
                await _notificationRepo.SaveChangesAsync();
            }
            catch
            {
                // A notification-write failure must not fail the review submission it's attached to.
            }
        }
        return true;
    }

    // History = every decision this lawyer has submitted, newest first.
    // decisionFilter: null/"All" | "Approved" | "EditedApproved" | "Rejected".
    // from/to bound ReviewedAt (inclusive) when given.
    public async Task<IReadOnlyList<ReviewHistoryItemDto>> GetHistoryAsync(
        int lawyerProfileId, string? decisionFilter = null, DateTime? from = null, DateTime? to = null)
    {
        var reviews = (await _reviewRepo.GetAllAsync())
            .Where(r => r.LawyerProfileId == lawyerProfileId);

        if (!string.IsNullOrWhiteSpace(decisionFilter) && decisionFilter != "All"
            && Enum.TryParse<ReviewDecision>(decisionFilter, out var decision))
            reviews = reviews.Where(r => r.Decision == decision);

        if (from.HasValue)
            reviews = reviews.Where(r => r.ReviewedAt >= from.Value);
        if (to.HasValue)
            reviews = reviews.Where(r => r.ReviewedAt <= to.Value);

        var result = new List<ReviewHistoryItemDto>();
        foreach (var r in reviews.OrderByDescending(r => r.ReviewedAt))
        {
            var d = await _docRepo.GetByIdAsync(r.DocumentId);
            if (d == null) continue;
            var c = await _caseRepo.GetByIdAsync(d.CaseId);
            if (c == null) continue;
            var category = await _categoryRepo.GetByIdAsync(c.CategoryId);
            var district = await _districtRepo.GetByIdAsync(c.DistrictId);

            result.Add(new ReviewHistoryItemDto(
                r.ReviewId, r.DocumentId, c.CaseId, SafeDecrypt(c.Title),
                category?.Name ?? "", district?.Name ?? "", r.Decision, r.Comments, r.ReviewedAt, d.VersionNo,
                d.ContentFinal ?? d.ContentDraft));
        }
        return result;
    }

    private static void ApplyDocumentDecision(GeneratedDocument d, DocumentStatus status, string? edited)
    {
        // Mirrors DocumentService.UpdateStatusAsync semantics (verified):
        // EditedApproved -> ContentFinal = edited; Approved -> final = draft.
        // Saved by the caller together with the review (see SubmitReviewAsync).
        d.Status = status;
        if (edited != null)
            d.ContentFinal = edited;
        else if (status == DocumentStatus.Approved)
            d.ContentFinal = d.ContentDraft;
    }

    // Case.Title/Description are field-level-encrypted PII (S-1.7). Decrypt
    // failures fall into two very different buckets:
    //   - genuine legacy plaintext rows (predate encryption): Decrypt throws
    //     immediately on the non-base64url text, and `value` IS the correct
    //     human-readable title -- must return it as-is.
    //   - orphaned ciphertext (e.g. a rotated/lost Data Protection key ring --
    //     see AUD-2): Decrypt throws too, but `value` is an opaque encrypted
    //     blob. Returning it verbatim used to leak raw ciphertext straight
    //     into the lawyer dashboard ("doc title coming crypted"). Detect that
    //     shape and show a safe placeholder instead of the blob.
    private static readonly Regex CiphertextShape = new(@"^[A-Za-z0-9\-_]{40,}$", RegexOptions.Compiled);
    private const string UndecryptablePlaceholder = "শিরোনাম উদ্ধার করা যায়নি / Title unavailable (decryption failed)";

    private string SafeDecrypt(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        try { return _encryptionService.Decrypt(value); }
        catch { return CiphertextShape.IsMatch(value) ? UndecryptablePlaceholder : value; }
    }
}
