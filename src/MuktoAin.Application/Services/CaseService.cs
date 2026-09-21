using System.Text.RegularExpressions;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

public class CaseService
{
    private readonly ICaseRepository _caseRepo;
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IRepository<District> _districtRepo;
    private readonly IEncryptionService _encryptionService;
    private readonly IRepository<Notification> _notificationRepo;

    public CaseService(
        ICaseRepository caseRepo,
        IRepository<CaseCategory> categoryRepo,
        IRepository<District> districtRepo,
        IEncryptionService encryptionService,
        IRepository<Notification> notificationRepo)
    {
        _caseRepo = caseRepo;
        _categoryRepo = categoryRepo;
        _districtRepo = districtRepo;
        _encryptionService = encryptionService;
        _notificationRepo = notificationRepo;
    }

    public async Task<CaseSubmissionResultDto> SubmitCaseAsync(CaseSubmissionDto dto, int? userId)
    {
        string? trackingCode = dto.IsAnonymous || userId == null
            ? Guid.NewGuid().ToString("N")
            : null;

        var caseEntity = new Case
        {
            UserId = dto.IsAnonymous ? null : userId,
            CategoryId = dto.CategoryId,
            DistrictId = dto.DistrictId,
            Title = _encryptionService.Encrypt(dto.Title),
            Description = _encryptionService.Encrypt(dto.Description),
            Language = dto.Language,
            Status = CaseStatus.Submitted,
            IsAnonymous = dto.IsAnonymous,
            AnonymousTrackingCode = trackingCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _caseRepo.AddAsync(caseEntity);
        await _caseRepo.SaveChangesAsync();

        if (userId.HasValue && !dto.IsAnonymous)
        {
            try
            {
                await _notificationRepo.AddAsync(new Notification
                {
                    UserId = userId.Value,
                    Type = NotificationType.CaseSubmitted,
                    RelatedCaseId = caseEntity.CaseId,
                    CreatedAt = DateTime.UtcNow
                });
                await _notificationRepo.SaveChangesAsync();
            }
            catch
            {
                // A notification-write failure must not fail the case submission it's attached to.
            }
        }

        return new CaseSubmissionResultDto(caseEntity.CaseId, trackingCode);
    }

    public async Task<CaseDetailDto?> GetCaseDetailAsync(int caseId, int? userId, UserRole callerRole, string? trackingCode = null)
    {
        var c = await _caseRepo.GetWithDocumentsAsync(caseId);
        if (c == null) return null;

        switch (callerRole)
        {
            case UserRole.Admin:
            case UserRole.Lawyer:
                break;
            case UserRole.Citizen:
                if (c.IsAnonymous || c.UserId == null)
                {
                    var codeValid = !string.IsNullOrEmpty(trackingCode)
                                    && c.AnonymousTrackingCode == trackingCode;
                    if (!codeValid) return null;
                }
                else if (userId == null || c.UserId != userId)
                {
                    return null;
                }
                break;
            default:
                return null;
        }

        return await MapToCaseDetailDtoAsync(c);
    }

    public async Task<IEnumerable<CaseDetailDto>> GetUserCasesAsync(int userId)
    {
        var cases = await _caseRepo.GetByUserIdAsync(userId);
        var result = new List<CaseDetailDto>();
        foreach (var c in cases)
        {
            result.Add(await MapToCaseDetailDtoAsync(c));
        }
        return result;
    }

    public async Task<bool> TransitionStatusAsync(int caseId, CaseStatus newStatus)
    {
        if (!await ApplyStatusTransitionAsync(caseId, newStatus)) return false;
        await _caseRepo.SaveChangesAsync();
        return true;
    }

    // Same as TransitionStatusAsync but leaves saving to the caller, so the
    // change can be committed together with other writes in one SaveChanges.
    public async Task<bool> ApplyStatusTransitionAsync(int caseId, CaseStatus newStatus)
    {
        var c = await _caseRepo.GetByIdAsync(caseId);
        if (c == null) return false;

        bool valid = (c.Status, newStatus) switch
        {
            (CaseStatus.Submitted, CaseStatus.UnderReview) => true,
            (CaseStatus.UnderReview, CaseStatus.Finalized) => true,
            (CaseStatus.UnderReview, CaseStatus.Submitted) => true,
            (CaseStatus.Finalized, CaseStatus.Submitted) => true,
            _ => false
        };

        if (!valid) return false;

        c.Status = newStatus;
        c.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private async Task<CaseDetailDto> MapToCaseDetailDtoAsync(Case c)
    {
        var category = await _categoryRepo.GetByIdAsync(c.CategoryId);
        var district = await _districtRepo.GetByIdAsync(c.DistrictId);

        return new CaseDetailDto(
            c.CaseId,
            SafeDecrypt(c.Title),
            SafeDecrypt(c.Description),
            category?.Name ?? string.Empty,
            district?.Name ?? string.Empty,
            c.Status.ToString(),
            c.IsAnonymous,
            c.CreatedAt,
            c.Documents.Select(d => new DraftDocumentDto(
                d.DocumentId,
                d.CaseId,
                d.DocumentType.ToString(),
                d.ContentDraft,
                d.Status.ToString(),
                d.CreatedAt)).ToList()
        );
    }

    // Two very different decrypt-failure buckets (see AUD-2 / S-1.7):
    //   - genuine legacy plaintext rows -- `value` IS the correct
    //     human-readable text, must be returned as-is.
    //   - orphaned ciphertext (rotated/lost Data Protection key ring) --
    //     `value` is an opaque encrypted blob. Returning it verbatim used to
    //     leak raw ciphertext straight into citizen/lawyer pages ("doc title
    //     coming crypted"). Detect that shape and show a safe placeholder.
    private static readonly Regex CiphertextShape = new(@"^[A-Za-z0-9\-_]{40,}$", RegexOptions.Compiled);
    private const string UndecryptablePlaceholder = "শিরোনাম উদ্ধার করা যায়নি / Title unavailable (decryption failed)";

    private string SafeDecrypt(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            return _encryptionService.Decrypt(value);
        }
        catch
        {
            // Graceful fallback for unencrypted legacy rows
            return CiphertextShape.IsMatch(value) ? UndecryptablePlaceholder : value;
        }
    }
}
