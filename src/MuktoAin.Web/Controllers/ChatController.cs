using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;

namespace MuktoAin.Web.Controllers;

// AJAX endpoints for the chat-first home page. Guests are keyed by the
// "mkt-chatkey" ASP.NET-session value (created on first contact).
[ApiController]
[Route("[controller]/[action]")]
public class ChatController : Controller
{
    private const string ChatKeySessionName = "mkt-chatkey";

    private readonly ChatService _chatService;
    private readonly AiBudgetService _budgetService;

    public ChatController(ChatService chatService, AiBudgetService budgetService)
    {
        _chatService = chatService;
        _budgetService = budgetService;
    }

    private int? CurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idStr, out var id) ? id : null;
    }

    private string? SessionKey()
    {
        var key = HttpContext.Session.GetString(ChatKeySessionName);
        if (string.IsNullOrEmpty(key))
        {
            key = Guid.NewGuid().ToString("N")[..22];
            HttpContext.Session.SetString(ChatKeySessionName, key);
        }
        return key;
    }

    // Start (or resume) a session. Body: { "firstMessage": "..." } (optional)
    [HttpPost]
    public async Task<IActionResult> New([FromBody] ChatNewRequest? body)
    {
        var session = await _chatService.GetOrCreateSessionAsync(
            CurrentUserId(), SessionKey(), body?.FirstMessage);
        return Json(new { chatSessionId = session.ChatSessionId, title = session.Title });
    }

    // Ask a question. Body: { chatSessionId, question, language? }
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] ChatAskRequest? body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Question) || body.ChatSessionId <= 0)
            return BadRequest(new { error = "question and chatSessionId required" });

        var userId = CurrentUserId();
        var key = userId.HasValue ? null : SessionKey();

        // Cache-first (spec: cache hits stretch the daily quota — a repeated
        // question must NOT be charged a turn or walled). AskAsync returns
        // FromCache=true when served from ANSWER_CACHE without any model call;
        // only a MISS consumes budget.
        var language = string.IsNullOrWhiteSpace(body.Language) ? "bn" : body.Language;
        var turn = await _chatService.AskAsync(body.ChatSessionId, body.Question, language, allowCapped: false);

        if (turn.FromCache)
        {
            var cacheQuota = await _budgetService.GetRemainingToday(userId, key);
            await _chatService.AppendMessageAsync(body.ChatSessionId, "user", body.Question, null);
            await _chatService.AppendMessageAsync(
                body.ChatSessionId, "assistant", turn.Answer, SerializeCited(turn.CitedSections));
            return Json(new
            {
                tier = turn.Tier,
                answer = turn.Answer,
                disclaimer = turn.Disclaimer,
                fromCache = true,
                retrievalOnly = turn.RetrievalOnly,
                citedSections = turn.CitedSections.Select(s => new
                {
                    sectionId = s.SectionId,
                    actTitle = s.ActTitle,
                    sectionNumber = s.SectionNumber,
                    sectionText = s.SectionText,
                    relevance = Math.Round(s.RelevanceScore * 100) + "%"
                }),
                remainingToday = cacheQuota.RemainingToday,
                dailyLimit = cacheQuota.DailyLimit
            });
        }

        // Cache miss. Retrieval-only answers make no model call (they're the
        // ladder's free tier — including when quota is exhausted mid-flight),
        // so only a real AI turn consumes budget.
        if (!turn.RetrievalOnly && !await _budgetService.TryReserveTurnAsync(userId, key))
        {
            var wall = await _budgetService.GetRemainingToday(userId, key);
            return Json(new
            {
                tier = "wall",
                remainingToday = wall.RemainingToday,
                dailyLimit = wall.DailyLimit
            });
        }

        var quota = turn.RetrievalOnly
            ? await _budgetService.GetRemainingToday(userId, key)
            : await _budgetService.RecordTurnUsed(userId, key);

        await _chatService.AppendMessageAsync(body.ChatSessionId, "user", body.Question, null);
        await _chatService.AppendMessageAsync(
            body.ChatSessionId, "assistant", turn.Answer, SerializeCited(turn.CitedSections));

        return Json(new
        {
            tier = turn.Tier,
            answer = turn.Answer,
            disclaimer = turn.Disclaimer,
            fromCache = turn.FromCache,
            retrievalOnly = turn.RetrievalOnly,
            citedSections = turn.CitedSections.Select(s => new
            {
                sectionId = s.SectionId,
                actTitle = s.ActTitle,
                sectionNumber = s.SectionNumber,
                sectionText = s.SectionText,
                relevance = Math.Round(s.RelevanceScore * 100) + "%"
            }),
            remainingToday = quota.RemainingToday,
            dailyLimit = quota.DailyLimit
        });
    }

    // Load a session's messages. Query: ?id=
    [HttpGet]
    public async Task<IActionResult> Messages(int id)
    {
        var session = await _chatService.GetSessionAsync(id);
        if (session == null) return NotFound();

        var userId = CurrentUserId();
        var key = SessionKey();
        var allowed = session.UserId == userId
                      || (session.UserId == null && session.SessionKey == key);
        if (!allowed) return Forbid();

        var messages = await _chatService.GetMessagesAsync(id);
        return Json(new
        {
            chatSessionId = id,
            title = session.Title,
            messages = messages.Select(m => new
            {
                role = m.Role, content = m.Content, citedJson = m.CitedJson
            })
        });
    }

    // Recent in-progress chats for the strip.
    [HttpGet]
    public async Task<IActionResult> Recent()
    {
        var userId = CurrentUserId();
        var key = userId.HasValue ? null : SessionKey();
        var recent = await _chatService.GetRecentAsync(userId, key);
        return Json(new
        {
            chats = recent.Select(r => new
            {
                chatSessionId = r.ChatSessionId,
                title = r.Title,
                updatedAt = r.UpdatedAt,
                messageCount = r.MessageCount
            })
        });
    }

    // Daily quota snapshot for the composer counter.
    [HttpGet]
    public async Task<IActionResult> Quota()
    {
        var userId = CurrentUserId();
        var key = userId.HasValue ? null : SessionKey();
        var snap = await _budgetService.GetRemainingToday(userId, key);
        return Json(new
        {
            remainingToday = snap.RemainingToday,
            dailyLimit = snap.DailyLimit,
            isLoggedIn = snap.IsLoggedIn
        });
    }

    // Generate Draft commit. Body: all modal fields.
    [HttpPost]
    public async Task<IActionResult> Commit([FromBody] ChatCommitRequest? body)
    {
        if (body == null || body.ChatSessionId <= 0 || body.CategoryId <= 0 || body.DistrictId <= 0)
            return BadRequest(new { error = "chatSessionId, categoryId, districtId required" });
        if (string.IsNullOrWhiteSpace(body.Title))
            return BadRequest(new { error = "title required" });

        try
        {
            var result = await _chatService.CommitToCaseAsync(
                body.ChatSessionId,
                body.CategoryId,
                body.DistrictId,
                body.Title,
                body.NotificationEmail,
                body.IsAnonymous,
                CurrentUserId(),
                body.DocumentType,
                HttpContext.RequestAborted);

            if (result.AnonymousTrackingCode != null)
                TempData["TrackingCode"] = result.AnonymousTrackingCode;

            return Json(new
            {
                caseId = result.CaseId,
                trackingCode = result.AnonymousTrackingCode,
                documentId = result.DocumentId,
                documentContent = result.DocumentContent,
                redirectUrl = Url.Action("Result", "Case",
                    new { id = result.CaseId, code = result.AnonymousTrackingCode })
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    private static string SerializeCited(IReadOnlyList<CitedSectionDto> sections)
    {
        var parts = sections.Select(s =>
            "{\"sectionId\":" + s.SectionId +
            ",\"actTitle\":\"" + s.ActTitle.Replace("\"", "\\\"") +
            "\",\"sectionNumber\":\"" + s.SectionNumber.Replace("\"", "\\\"") + "\"}");
        return "[" + string.Join(",", parts) + "]";
    }
}

// ---- request bodies ----
public class ChatNewRequest
{
    public string? FirstMessage { get; set; }
}

public class ChatAskRequest
{
    public int ChatSessionId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string? Language { get; set; }
}

public class ChatCommitRequest
{
    public int ChatSessionId { get; set; }
    public int CategoryId { get; set; }
    public byte DistrictId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? NotificationEmail { get; set; }
    public bool IsAnonymous { get; set; }
    public string DocumentType { get; set; } = "LabourComplaint";
}
