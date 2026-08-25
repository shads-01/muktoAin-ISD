using Microsoft.AspNetCore.Mvc;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

public class CaseController : Controller
{
    // CP2: Shads/Arpita will inject real services here
    // private readonly CaseService _caseService;
    // private readonly AiOrchestrationService _aiService;

    [HttpGet]
    public IActionResult Submit()
    {
        var vm = new CaseSubmitViewModel
        {
            Categories = MockData.Categories,
            Districts = MockData.Districts
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Submit(CaseSubmitViewModel vm)
    {
        // TODO: [Arpita] Replace with CaseService.SubmitCaseAsync()
        // TODO: [Shads] Wire AiOrchestrationService for rights explanation
        TempData["Success"] = "মামলা সফলভাবে জমা হয়েছে! ট্র্যাকিং কোড: MKT-2026-0042";
        return RedirectToAction("Result", new { id = 42 });
    }

    [HttpGet]
    public IActionResult Result(int id)
    {
        return View(MockData.SampleCaseResult);
    }

    [HttpGet]
    public IActionResult Track()
    {
        var vm = new CaseTrackViewModel
        {
            Cases = MockData.SampleCases
        };
        return View(vm);
    }
}
