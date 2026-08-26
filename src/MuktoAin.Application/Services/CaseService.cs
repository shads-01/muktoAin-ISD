using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

public class CaseService
{
    private readonly ICaseRepository _caseRepo;
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IRepository<District> _districtRepo;

    public CaseService(
        ICaseRepository caseRepo,
        IRepository<CaseCategory> categoryRepo,
        IRepository<District> districtRepo)
    {
        _caseRepo = caseRepo;
        _categoryRepo = categoryRepo;
        _districtRepo = districtRepo;
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
            Title = dto.Title,
            Description = dto.Description,
            Language = dto.Language,
            Status = CaseStatus.Submitted,
            IsAnonymous = dto.IsAnonymous,
            AnonymousTrackingCode = trackingCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _caseRepo.AddAsync(caseEntity);
        await _caseRepo.SaveChangesAsync();

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
                if (userId == null)
                {
                    var codeValid = !string.IsNullOrEmpty(trackingCode)
                                    && c.AnonymousTrackingCode == trackingCode;
                    if (!(codeValid && c.UserId == null)) return null;
                }
                else if (c.IsAnonymous || c.UserId != userId)
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
        await _caseRepo.SaveChangesAsync();
        return true;
    }

    private async Task<CaseDetailDto> MapToCaseDetailDtoAsync(Case c)
    {
        var category = await _categoryRepo.GetByIdAsync(c.CategoryId);
        var district = await _districtRepo.GetByIdAsync(c.DistrictId);

        return new CaseDetailDto(
            c.CaseId,
            c.Title,
            c.Description,
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
}
