using Microsoft.AspNetCore.Mvc;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

public class LawyerController : Controller
{
    [HttpGet]
    public IActionResult Queue()
    {
        // TODO: [Shads/Arpita] Replace with real document review queue query
        var cases = MockData.SampleCases;
        return View(cases);
    }

    [HttpGet]
    public IActionResult Review(int id)
    {
        // TODO: [Arpita] Replace with DocumentService.GetDocumentForReviewAsync()
        return View(MockData.SampleLawyerReview);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SubmitReview(LawyerReviewViewModel vm)
    {
        // TODO: [Arpita] Replace with LawyerReviewService.SubmitReviewAsync()
        TempData["Success"] = $"দলিল #{vm.DocumentId} সফলভাবে পর্যালোচনা সম্পন্ন হয়েছে (সিদ্ধান্ত: {vm.Decision})।";
        return RedirectToAction("Queue");
    }
}
