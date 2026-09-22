using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Application.Services;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

[Authorize]
public class NotificationController : Controller
{
    private const int PageSize = 20;

    private readonly NotificationService _service;

    public NotificationController(NotificationService service)
    {
        _service = service;
    }

    private int CurrentUserId() =>
        int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> Unread()
    {
        var userId = CurrentUserId();
        var count = await _service.GetUnseenCountAsync(userId);
        var recent = await _service.GetRecentAsync(userId, take: 10);

        return Json(new
        {
            count,
            items = recent.Select(n =>
            {
                var (textBn, textEn, url) = NotificationTextFormatter.Format(n);
                return new { id = n.NotificationId, textBn, textEn, url, isRead = n.IsRead, createdAt = n.CreatedAt };
            })
        });
    }

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = CurrentUserId();
        var paged = await _service.GetPagedAsync(userId, page, PageSize);

        var vm = new NotificationListViewModel
        {
            Page = page,
            PageSize = PageSize,
            TotalCount = paged.TotalCount,
            Items = paged.Items.Select(n =>
            {
                var (textBn, textEn, url) = NotificationTextFormatter.Format(n);
                return new NotificationItemViewModel
                {
                    NotificationId = n.NotificationId,
                    TextBn = textBn,
                    TextEn = textEn,
                    Url = url,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                };
            }).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        var ok = await _service.MarkReadAsync(id, CurrentUserId());
        if (!ok) return Forbid();
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var ok = await _service.DeleteAsync(id, CurrentUserId());
        if (!ok) return Forbid();
        return Ok();
    }

    // Called by the bell dropdown when it opens: clears the badge count
    // (IsSeen) without marking anything read, so per-item unread styling and
    // My Cases' unread dot survive until the notification is actually opened.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllSeen()
    {
        await _service.MarkAllSeenAsync(CurrentUserId());
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        await _service.MarkAllReadAsync(CurrentUserId());
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> MarkAndGo(int id, string returnUrl)
    {
        await _service.MarkReadAsync(id, CurrentUserId());
        if (!Url.IsLocalUrl(returnUrl)) return RedirectToAction(nameof(Index));
        return Redirect(returnUrl);
    }
}
