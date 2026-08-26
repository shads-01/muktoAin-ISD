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
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IRepository<District> _districtRepo;

    public CaseController(
        CaseService caseService,
        IRepository<CaseCategory> categoryRepo,
        IRepository<District> districtRepo)
    {
        _caseService = caseService;
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

        // Identity not wired yet (S-1.1 pending) -- submissions are guest submissions.
        var result = await _caseService.SubmitCaseAsync(dto, userId: null);

        RememberTrackedCase(result.CaseId, result.AnonymousTrackingCode);

        TempData["Success"] = "মামলা সফলভাবে জমা হয়েছে!";
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
        if (trackingCode == null) return NotFound();

        var detail = await _caseService.GetCaseDetailAsync(id, userId: null, UserRole.Citizen, trackingCode);
        if (detail == null) return NotFound();

        return View(MapToResultViewModel(detail));
    }

    [HttpGet]
    public async Task<IActionResult> Track()
    {
        var vm = new CaseTrackViewModel();

        foreach (var (caseId, code) in GetTrackedCases())
        {
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
        return new CaseResultViewModel
        {
            CaseId = detail.CaseId,
            Title = detail.Title,
            Status = detail.Status,
            CategoryName = detail.CategoryName,
            DistrictName = detail.DistrictName,
            CreatedAt = detail.CreatedAt,
            RightsExplanation = string.Empty
        };
    }
}
