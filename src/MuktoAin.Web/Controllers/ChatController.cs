using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.Models;
using MuktoAin.Web.Session;

namespace MuktoAin.Web.Controllers;

// AJAX endpoints for the chat-first home page. Guests are keyed by the
// "mkt-chatkey" ASP.NET-session value (created on first contact).
[ApiController]
[Route("[controller]/[action]")]
public class ChatController : Controller
{
    public const int MaxQuestionLength = 2000;

    private const string ChatKeySessionName = "mkt-chatkey";

    private readonly ChatService _chatService;
    private readonly AiBudgetService _budgetService;
    private readonly IActSectionRepository _sectionRepo;

    public ChatController(ChatService chatService, AiBudgetService budgetService,
        IActSectionRepository sectionRepo)
    {
        _chatService = chatService;
        _budgetService = budgetService;
        _sectionRepo = sectionRepo;
    }

    private int? CurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idStr, out var id) ? id : null;
    }

    private bool OwnsSession(ChatSession session, int? userId, string? key)
        => ChatService.OwnsSession(session, userId, key);

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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New([FromBody] ChatNewRequest? body)
    {
        var session = await _chatService.GetOrCreateSessionAsync(
            CurrentUserId(), SessionKey(), body?.FirstMessage, body?.NewChat ?? false);
        return Json(new { chatSessionId = session.ChatSessionId, title = session.Title });
    }

    // Ask a question. Body: { chatSessionId, question, language? }
    [HttpPost]
    [EnableRateLimiting("chat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask([FromBody] ChatAskRequest? body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Question) || body.ChatSessionId <= 0)
            return BadRequest(new { error = "question and chatSessionId required" });

        if (body.Question.Length > MaxQuestionLength)
            return BadRequest(new
            {
                error = "Your message is too long. Please keep it under 2000 characters and try again.",
                errorBn = "আপনার বার্তাটি অনেক দীর্ঘ। অনুগ্রহ করে ২০০০ অক্ষরের মধ্যে লিখে আবার চেষ্টা করুন।"
            });

        var session = await _chatService.GetSessionAsync(body.ChatSessionId);
        if (session == null)
            return NotFound(new { error = "Session not found" });

        var userId = CurrentUserId();
        var key = SessionKey();
        if (!OwnsSession(session, userId, key)) return Forbid();

        if (session.Status == ChatSessionStatus.Committed)
            return Conflict(new { error = "Committed chats are read-only." });

        var language = string.IsNullOrWhiteSpace(body.Language) ? "bn" : body.Language;

        // A2: standalone section-search mode (FR-7). Pure keyword retrieval —
        // no model call, so it never touches the quota ladder. Messages are
        // still persisted so the thread replays coherently.
        if (string.Equals(body.Mode, "search", StringComparison.OrdinalIgnoreCase))
        {
            var searchTurn = await _chatService.SearchSectionsAsync(body.Question, language);

            await _chatService.AppendMessageAsync(body.ChatSessionId, "user", body.Question, null);
            await _chatService.AppendMessageAsync(
                body.ChatSessionId, "assistant", searchTurn.Answer, SerializeCited(searchTurn.CitedSections));

            return Json(new
            {
                tier = searchTurn.Tier,
                answer = searchTurn.Answer,
                disclaimer = searchTurn.Disclaimer,
                fromCache = false,
                retrievalOnly = true,
                blocked = false,
                canDraft = false,
                suggestedCategoryId = (int?)null,
                caseFileJson = (string?)null,
                missingInfo = (IReadOnlyList<string>?)null,
                citedSections = searchTurn.CitedSections.Select(s => new
                {
                    sectionId = s.SectionId,
                    actTitle = s.ActTitle,
                    sectionNumber = s.SectionNumber,
                    sectionText = s.SectionText,
                    relevance = Math.Round(s.RelevanceScore * 100) + "%"
                }),
                remainingToday = (int?)null,
                dailyLimit = (int?)null
            });
        }

        // Cache-first (A1: cache hits stretch the daily quota — a repeated
        // question is served from ANSWER_CACHE without any model call;
        // only a MISS consumes budget).
        // AUD-3: reserve BEFORE the metered call so two concurrent asks
        // cannot both pass a stale count (TOCTOU). The reservation (a free
        // turn, else a paid chat credit) is kept only when a model call was
        // made; cache hits, retrieval-only and heuristic-blocked turns are
        // free (spec 6) and release it, which also gives back the credit.
        var reservation = await _budgetService.TryReserveTurnAsync(userId, key);
        if (reservation == null)
        {
            var wall = await _budgetService.GetRemainingToday(userId, key);
            return Json(new
            {
                tier = "wall",
                remainingToday = wall.RemainingToday,
                dailyLimit = wall.DailyLimit,
                credits = wall.Credits,
                isLoggedIn = wall.IsLoggedIn
            });
        }

        ChatTurnDto turn;
        try
        {
            turn = await _chatService.AskAsync(body.ChatSessionId, body.Question, language);
        }
        catch
        {
            // No answer reached the citizen: do not charge the turn or credit.
            await _budgetService.ReleaseReservationAsync(reservation);
            throw;
        }

        if (turn.FromCache || turn.RetrievalOnly || turn.Blocked)
            await _budgetService.ReleaseReservationAsync(reservation);

        var quota = await _budgetService.RecordTurnUsed(userId, key);

        await _chatService.AppendMessageAsync(body.ChatSessionId, "user", body.Question, null);
        await _chatService.AppendMessageAsync(
            body.ChatSessionId, "assistant", turn.Answer, SerializeCited(turn.CitedSections));

        var isSessionBlocked = session.Status == ChatSessionStatus.Blocked || session.BlockedStreak >= 3;

        return Json(new
        {
            tier = turn.Tier,
            answer = turn.Answer,
            disclaimer = turn.Disclaimer,
            fromCache = turn.FromCache,
            retrievalOnly = turn.RetrievalOnly,
            blocked = turn.Blocked,
            sessionBlocked = isSessionBlocked,
            canDraft = !isSessionBlocked && turn.CanDraft,
            suggestedCategoryId = turn.SuggestedCategoryId,
            caseFileJson = turn.CaseFileJson,
            missingInfo = turn.MissingInfo,
            citedSections = turn.CitedSections.Select(s => new
            {
                sectionId = s.SectionId,
                actTitle = s.ActTitle,
                sectionNumber = s.SectionNumber,
                sectionText = s.SectionText,
                relevance = Math.Round(s.RelevanceScore * 100) + "%"
            }),
            remainingToday = quota.RemainingToday,
            dailyLimit = quota.DailyLimit,
            credits = quota.Credits
        });
    }

    // A6: citation text on demand — replayed messages carry only
    // sectionId/actTitle/sectionNumber in CitedJson, so the modal fetches the
    // authoritative statutory text when it opens.
    [HttpGet]
    public async Task<IActionResult> Citation(int id)
    {
        var sections = await _sectionRepo.GetBySectionIdsAsync(new[] { id });
        var s = sections.FirstOrDefault();
        if (s == null) return NotFound(new { error = "Section not found" });

        return Json(new
        {
            sectionId = s.SectionId,
            actTitle = s.Act.Title,
            sectionNumber = s.SectionNumber,
            sectionText = s.SectionText
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
        if (!OwnsSession(session, userId, key)) return Forbid();

        var messages = await _chatService.GetMessagesAsync(id);

        // A5: recompute draft eligibility from the persisted case file so the
        // draft card reappears on resume (missingInfo stays ephemeral — it was
        // only ever a per-turn envelope hint).
        var cfJson = session.CaseFileJson;
        var categoryId = ChatService.MapCategory(ChatService.CaseFileString(cfJson, "category"));
        var hasDistrict = !string.IsNullOrWhiteSpace(ChatService.CaseFileString(cfJson, "district"));
        var committed = session.Status == ChatSessionStatus.Committed;
        var blocked = session.Status == ChatSessionStatus.Blocked;
        var canDraft = !committed && !blocked && messages.Count > 0 && categoryId.HasValue && hasDistrict;

        string? caseUrl = null;
        if (committed)
        {
            var linkedCase = await _chatService.GetOwnedCommittedCaseAsync(id, userId, key);
            if (linkedCase != null)
            {
                TrackedCases.Remember(HttpContext.Session, linkedCase.CaseId, linkedCase.AnonymousTrackingCode);
                caseUrl = Url.Action("Result", "Case", new { id = linkedCase.CaseId });
            }
        }

        return Json(new
        {
            chatSessionId = id,
            title = session.Title,
            caseFileJson = session.CaseFileJson,
            language = session.Language,
            canDraft = canDraft,
            committed,
            blocked,
            caseId = committed ? session.CommittedCaseId : null,
            caseUrl,
            suggestedCategoryId = categoryId,
            messages = messages.Select(m => new
            {
                role = m.Role, content = m.Content, citedJson = m.CitedJson
            })
        });
    }

    [HttpGet]
    public async Task<IActionResult> Recent(DateTime? beforeUpdatedAt = null, int? beforeId = null)
    {
        if (beforeUpdatedAt.HasValue != beforeId.HasValue || beforeId is <= 0)
            return BadRequest(new { error = "Both cursor fields are required and id must be positive." });
        var userId = CurrentUserId();
        var page = await _chatService.GetRecentAsync(userId,
            userId.HasValue ? null : SessionKey(), beforeUpdatedAt, beforeId,
            HttpContext.RequestAborted);
        return Json(new
        {
            chats = page.Chats.Select(r => new
            {
                chatSessionId = r.ChatSessionId, title = r.Title, updatedAt = r.UpdatedAt,
                messageCount = r.MessageCount, status = r.Status, caseId = r.CaseId
            }),
            nextCursor = page.BeforeId.HasValue
                ? new { beforeUpdatedAt = page.BeforeUpdatedAt, beforeId = page.BeforeId }
                : null
        });
    }

    // Permanently delete one of the caller's own chats. Body: { "chatSessionId": n }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromBody] ChatDeleteRequest? body)
    {
        if (body == null || body.ChatSessionId <= 0)
            return BadRequest(new { error = "chatSessionId required" });

        var session = await _chatService.GetSessionAsync(body.ChatSessionId);
        if (session == null)
            return NotFound(new { error = "Session not found" });

        if (!OwnsSession(session, CurrentUserId(), SessionKey())) return Forbid();

        await _chatService.DeleteSessionAsync(session);
        return Json(new { deleted = true, chatSessionId = body.ChatSessionId });
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
            isLoggedIn = snap.IsLoggedIn,
            credits = snap.Credits
        });
    }

    // Generate Draft commit (read-only confirm card — spec 3.5). Category,
    // district and title are optional: CommitToCaseAsync resolves them from
    // the session's case file; the form path (CaseController) passes them
    // explicitly. Missing case-file fields surface as an error telling the
    // citizen to supply them in the chat.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Commit([FromBody] ChatCommitRequest? body)
    {
        if (body == null || body.ChatSessionId <= 0)
            return BadRequest(new { error = "chatSessionId required" });

        var session = await _chatService.GetSessionAsync(body.ChatSessionId);
        if (session == null)
            return NotFound(new { error = "Session not found" });

        var userId = CurrentUserId();
        var key = SessionKey();
        if (!OwnsSession(session, userId, key)) return Forbid();

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
                body.Language,
                HttpContext.RequestAborted);

            var linkedCase = await _chatService.GetOwnedCommittedCaseAsync(body.ChatSessionId, userId, key);
            if (linkedCase == null || linkedCase.CaseId != result.CaseId)
                return Conflict(new { error = "Committed case is unavailable." });
            TrackedCases.Remember(HttpContext.Session, linkedCase.CaseId, linkedCase.AnonymousTrackingCode);
            if (result.AnonymousTrackingCode != null)
                TempData["TrackingCode"] = result.AnonymousTrackingCode;
            return Json(new
            {
                caseId = result.CaseId,
                documentId = result.DocumentId,
                documentContent = result.DocumentContent,
                redirectUrl = Url.Action("Result", "Case", new { id = result.CaseId })
            });
        }
        catch (Exception ex)
        {
            HttpContext?.RequestServices?.GetService<ILogger<ChatController>>()
                ?.LogError(ex, "Chat commit failed for session {ChatSessionId}", body.ChatSessionId);
            return Json(new
            {
                success = false,
                error = ApiErrors.AiUnavailableEn,
                errorBn = ApiErrors.AiUnavailableBn,
                message = "AI_REQUEST_FAILED",
            });
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
    public bool NewChat { get; set; }
}

public class ChatAskRequest
{
    public int ChatSessionId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string? Language { get; set; }

    // A2: "rights" (default) | "search" — search routes to keyword section
    // retrieval (FR-7), no model call, no quota.
    public string? Mode { get; set; }
}

public class ChatDeleteRequest
{
    public int ChatSessionId { get; set; }
}

public class ChatCommitRequest
{
    public int ChatSessionId { get; set; }
    public int CategoryId { get; set; }          // optional on the chat path — resolved from the case file
    public byte DistrictId { get; set; }         // optional on the chat path
    public string? Title { get; set; }           // optional on the chat path
    public string? NotificationEmail { get; set; }
    public bool IsAnonymous { get; set; }
    public string? Language { get; set; }
}
