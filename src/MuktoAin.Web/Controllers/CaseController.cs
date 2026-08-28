using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

public class CaseController : Controller
{
    private const string TrackedCasesKey = "TrackedCases";

    private readonly CaseService _caseService;
    private readonly IRightsExplanationService _rightsExplanationService;
    private readonly DocumentService _documentService;
    private readonly ICaseRepository _caseRepo;
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IRepository<District> _districtRepo;

    public CaseController(
        CaseService caseService,
        IRightsExplanationService rightsExplanationService,
        DocumentService documentService,
        ICaseRepository caseRepo,
        IRepository<CaseCategory> categoryRepo,
        IRepository<District> districtRepo)
    {
        _caseService = caseService;
        _rightsExplanationService = rightsExplanationService;
        _documentService = documentService;
        _caseRepo = caseRepo;
        _categoryRepo = categoryRepo;
        _districtRepo = districtRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Submit()
    {
        var vm = await BuildSubmitViewModelAsync();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(CaseSubmitViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(vm);
            return View(vm);
        }

        var dto = new CaseSubmissionDto(
            vm.CategoryId,
            vm.DistrictId,
            vm.Title,
            vm.Description,
            vm.Language,
            vm.IsAnonymous);

        var currentUserId = GetCurrentUserId();
        var result = await _caseService.SubmitCaseAsync(dto, currentUserId);

        RememberTrackedCase(result.CaseId, result.AnonymousTrackingCode);

        TempData["Success"] = "মামলা সফলভাবে জমা হয়েছে!";
        TempData["SuccessEn"] = "Case submitted successfully!";
        if (result.AnonymousTrackingCode != null)
        {
            TempData["TrackingCode"] = result.AnonymousTrackingCode;
        }

        return RedirectToAction(nameof(Result), new { id = result.CaseId });
    }

    [HttpGet]
    public async Task<IActionResult> Result(int id, string? code)
    {
        var trackingCode = ResolveTrackingCode(id, code);
        var currentUserId = GetCurrentUserId();
        var role = GetCurrentUserRole();

        var detail = await _caseService.GetCaseDetailAsync(id, currentUserId, role, trackingCode);
        if (detail == null) return NotFound();

        var vm = MapToResultViewModel(detail);

        var caseEntity = await _caseRepo.GetByIdAsync(id);
        if (caseEntity != null)
        {
            if (caseEntity.District == null)
            caseEntity.District = await _districtRepo.GetByIdAsync(caseEntity.DistrictId);
        if (caseEntity.Category == null)
            caseEntity.Category = await _categoryRepo.GetByIdAsync(caseEntity.CategoryId);
        
                var explanation = await _rightsExplanationService.ExplainRightsAsync(caseEntity);
                vm.RightsExplanation = explanation.Explanation;
                vm.CitedSections = explanation.CitedSections
                    .Select(s => new CitedSectionViewModel
                    {
                        ActTitle = s.ActTitle,
                        SectionNumber = string.IsNullOrWhiteSpace(s.SectionNumber) ? string.Empty : $"ধারা {s.SectionNumber}",
                        SectionText = s.SectionText,
                        RelevanceScore = $"{Math.Round(s.RelevanceScore * 100)}%"
                    })
                    .ToList();
                    try{

                //if (vm.DocumentId == null)
                
                    var docDto = await _documentService.GenerateDocumentAsync(id, explanation);
                    vm.DocumentId = docDto.DocumentId;
                    vm.DocumentContent = docDto.ContentDraft;
                    vm.DocumentStatus = docDto.Status;
                    vm.CanDownloadPdf = docDto.Status == nameof(DocumentStatus.Approved);
                }
            //}
            //catch
            //{
              //  vm.RightsExplanation = "আইনি অধিকার বিশ্লেষণ প্রস্তুত হচ্ছে... অনুগ্রহ করে কিছুক্ষণ পর পুনরায় পৃষ্ঠাটি রিফ্রেশ করুন।";
            //}
        //}
        catch (Exception ex)
        {
            var generator = HttpContext.RequestServices.GetRequiredService<MuktoAin.Application.Documents.DocumentGenerator>();
            vm.DocumentContent = await generator.GenerateAsync(caseEntity, explanation);
            vm.DocumentId = 1;
            vm.DocumentStatus = "Draft";
            Console.WriteLine($"[DocumentService DB Error]: {ex.Message}");
        }

        vm.CanDownloadPdf = false;
    }

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Track()
    {
        var vm = new CaseTrackViewModel();

        var currentUserId = GetCurrentUserId();
        var role = GetCurrentUserRole();

        if (currentUserId.HasValue)
        {
            var userCases = await _caseService.GetUserCasesAsync(currentUserId.Value);
            foreach (var detail in userCases)
            {
                vm.Cases.Add(new CaseListItemViewModel
                {
                    CaseId = detail.CaseId,
                    TrackingCode = string.Empty,
                    Title = detail.Title,
                    CategoryName = detail.CategoryName,
                    Status = detail.Status,
                    CreatedAt = detail.CreatedAt
                });
            }
        }

        foreach (var (caseId, code) in GetTrackedCases())
        {
            if (vm.Cases.Any(c => c.CaseId == caseId)) continue;

            var detail = await _caseService.GetCaseDetailAsync(caseId, userId: null, UserRole.Citizen, code);
            if (detail == null) continue;

            vm.Cases.Add(new CaseListItemViewModel
            {
                CaseId = detail.CaseId,
                TrackingCode = code,
                Title = detail.Title,
                CategoryName = detail.CategoryName,
                Status = detail.Status,
                CreatedAt = detail.CreatedAt
            });
        }

        return View(vm);
    }

    private async Task<CaseSubmitViewModel> BuildSubmitViewModelAsync()
    {
        var vm = new CaseSubmitViewModel();
        await PopulateDropdownsAsync(vm);
        return vm;
    }

    private async Task PopulateDropdownsAsync(CaseSubmitViewModel vm)
    {
        var categories = await _categoryRepo.GetAllAsync();
        var districts = await _districtRepo.GetAllAsync();

        vm.Categories = categories
            .OrderBy(c => c.CategoryId)
            .Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.Name })
            .ToList();

        vm.Districts = districts
            .OrderBy(d => d.Name)
            .Select(d => new SelectListItem { Value = d.DistrictId.ToString(), Text = d.Name })
            .ToList();
    }

    private string? ResolveTrackingCode(int caseId, string? queryCode)
    {
        if (!string.IsNullOrEmpty(queryCode)) return queryCode;
        if (TempData.Peek("TrackingCode") is string tempCode) return tempCode;
        return GetTrackedCases().FirstOrDefault(t => t.caseId == caseId).code;
    }

    private void RememberTrackedCase(int caseId, string? code)
    {
        if (code == null) return;
        var entries = GetTrackedCases();
        if (entries.Any(t => t.caseId == caseId)) return;
        entries.Add((caseId, code));
        SaveTrackedCases(entries);
    }

    private List<(int caseId, string code)> GetTrackedCases()
    {
        var raw = HttpContext.Session.GetString(TrackedCasesKey);
        if (string.IsNullOrEmpty(raw)) return new List<(int, string)>();

        return raw.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Split(':', 2))
            .Where(p => p.Length == 2 && int.TryParse(p[0], out _))
            .Select(p => (int.Parse(p[0]), p[1]))
            .ToList();
    }

    private void SaveTrackedCases(List<(int caseId, string code)> entries)
    {
        HttpContext.Session.SetString(TrackedCasesKey,
            string.Join("|", entries.Select(t => $"{t.caseId}:{t.code}")));
    }

    private static CaseResultViewModel MapToResultViewModel(CaseDetailDto detail)
    {
        var existingDoc = detail.Documents?.LastOrDefault();
        return new CaseResultViewModel
        {
            CaseId = detail.CaseId,
            Title = detail.Title,
            Status = detail.Status,
            CategoryName = detail.CategoryName,
            DistrictName = detail.DistrictName,
            CreatedAt = detail.CreatedAt,
            RightsExplanation = string.Empty,
            DocumentId = existingDoc?.DocumentId,
            DocumentContent = existingDoc?.ContentDraft,
            DocumentStatus = existingDoc?.Status,
            CanDownloadPdf = existingDoc?.Status == nameof(DocumentStatus.Approved)
        };
    }

    private int? GetCurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idStr, out var id) ? id : null;
    }

    private UserRole GetCurrentUserRole()
    {
        if (User.IsInRole(nameof(UserRole.Admin))) return UserRole.Admin;
        if (User.IsInRole(nameof(UserRole.Lawyer))) return UserRole.Lawyer;
        return UserRole.Citizen;
    }
}
