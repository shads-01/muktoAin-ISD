using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Application.Services;

// Chat-first home (FR-19). Sessions stay InProgress until the citizen presses
// [Generate Draft]; CommitToCaseAsync then creates the case + document and
// flips the session to Committed. Chat turns run on an UNSAVED Case (CaseId=0)
// so the shared pipeline's case-scoped DB writes stay inert until commit.
//
// Conversational redesign (spec: docs/superpowers/specs/2026-09-15-conversational-chat-redesign-design.md):
// each AskAsync turn = safety pre-filter → intake envelope call (no RAG) →
// structured state update → optional explain turn (existing RAG pipeline).
public class ChatService
{
    private const string IntakeModelName = "gemini-2.5-flash";
    private const int BlockedStreakLockThreshold = 3;

    private readonly IRepository<ChatSession> _sessionRepo;
    private readonly IRepository<ChatMessage> _messageRepo;
    private readonly IRepository<Case> _caseRepo;
    private readonly ICaseRepository _caseRepoTyped;
    private readonly IRepository<AnswerCache> _cacheRepo;
    private readonly IRightsExplanationService _rightsService;
    private readonly DocumentService _documentService;
    private readonly IEncryptionService _encryptionService;
    private readonly IScenarioMappingRepository _scenarioRepo;
    private readonly IKeywordSectionSearch _keywordSearch;
    private readonly IRepository<District> _districtRepo;
    private readonly IAiService _aiService;
    private readonly IAiLogService _aiLogService;
    private readonly IChatHistoryRepository _historyRepo;
    private readonly ChatSafetyFilter _safetyFilter = new();
    private readonly IRepository<Notification> _notificationRepo;

    public ChatService(
        IRepository<ChatSession> sessionRepo,
        IRepository<ChatMessage> messageRepo,
        IRepository<Case> caseRepo,
        ICaseRepository caseRepoTyped,
        IRepository<AnswerCache> cacheRepo,
        IRightsExplanationService rightsService,
        DocumentService documentService,
        IEncryptionService encryptionService,
        IScenarioMappingRepository scenarioRepo,
        IKeywordSectionSearch keywordSearch,
        IRepository<District> districtRepo,
        IAiService aiService,
        IAiLogService aiLogService,
        IChatHistoryRepository historyRepo,
        IRepository<Notification> notificationRepo)
    {
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
        _caseRepo = caseRepo;
        _caseRepoTyped = caseRepoTyped;
        _cacheRepo = cacheRepo;
        _rightsService = rightsService;
        _documentService = documentService;
        _encryptionService = encryptionService;
        _scenarioRepo = scenarioRepo;
        _keywordSearch = keywordSearch;
        _districtRepo = districtRepo;
        _aiService = aiService;
        _aiLogService = aiLogService;
        _historyRepo = historyRepo;
        _notificationRepo = notificationRepo;
    }

    // ---------- session management ----------

    public static bool OwnsSession(ChatSession session, int? userId, string? key)
        => (userId.HasValue && session.UserId == userId.Value)
           || (session.UserId == null && !string.IsNullOrEmpty(key) && session.SessionKey == key);

    public async Task<ChatSession> GetOrCreateSessionAsync(
        int? userId, string? sessionKey, string? firstMessage, bool newChat = false)
    {
        if (!newChat)
        {
            var all = await _sessionRepo.GetAllAsync();
            var existing = all.Where(s => s.Status == ChatSessionStatus.InProgress &&
                    (userId.HasValue ? s.UserId == userId.Value :
                        s.UserId == null && !string.IsNullOrEmpty(sessionKey) && s.SessionKey == sessionKey))
                .OrderByDescending(s => s.UpdatedAt).ThenByDescending(s => s.ChatSessionId)
                .FirstOrDefault();
            if (existing != null) return existing;
        }

        var session = new ChatSession
        {
            UserId = userId,
            SessionKey = userId.HasValue ? null : (sessionKey ?? Guid.NewGuid().ToString("N")[..22]),
            Title = BuildTitle(firstMessage),
            Status = ChatSessionStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _sessionRepo.AddAsync(session);
        await _sessionRepo.SaveChangesAsync();
        return session;
    }

    private static string BuildTitle(string? firstMessage)
    {
        if (string.IsNullOrWhiteSpace(firstMessage)) return "নতুন আলোচনা";
        return firstMessage.Length > 60 ? firstMessage[..60] + "…" : firstMessage;
    }

    public async Task<ChatHistoryPageDto> GetRecentAsync(
        int? userId, string? sessionKey, DateTime? beforeUpdatedAt = null,
        int? beforeId = null, CancellationToken ct = default)
    {
        if (beforeUpdatedAt.HasValue != beforeId.HasValue || beforeId is <= 0)
            throw new ArgumentException("Both cursor fields are required and id must be positive.");
        var rows = await _historyRepo.GetPageAsync(
            userId, userId.HasValue ? null : sessionKey, beforeUpdatedAt, beforeId, ct);
        var chats = rows.Take(25).Select(s => new RecentChatDto(
            s.ChatSessionId, s.Title, DateTime.SpecifyKind(s.UpdatedAt, DateTimeKind.Utc),
            s.MessageCount, s.Status.ToString(), s.CaseId)).ToList();
        var last = rows.Count > 25 ? chats[^1] : null;
        return new ChatHistoryPageDto(chats, last?.UpdatedAt, last?.ChatSessionId);
    }

    public async Task<ChatSession?> GetSessionAsync(int chatSessionId)
        => await _sessionRepo.GetByIdAsync(chatSessionId);

    // Hard delete: CHAT_MESSAGE rows go via the FK's ON DELETE CASCADE. A committed
    // session's Case/document are not referenced from the session's side, so they stay.
    public async Task DeleteSessionAsync(ChatSession session)
    {
        await _sessionRepo.DeleteAsync(session);
        await _sessionRepo.SaveChangesAsync();
    }

    public async Task<Case?> GetOwnedCommittedCaseAsync(int chatSessionId, int? userId, string? key)
    {
        var session = await _sessionRepo.GetByIdAsync(chatSessionId);
        if (session == null || !OwnsSession(session, userId, key) ||
            session.Status != ChatSessionStatus.Committed || !session.CommittedCaseId.HasValue)
            return null;
        var result = await _caseRepoTyped.GetWithDocumentsAsync(session.CommittedCaseId.Value);
        if (result == null || result.CaseId != session.CommittedCaseId.Value) return null;
        if (result.IsAnonymous || result.UserId == null)
        {
            if (result.UserId.HasValue && result.UserId != userId) return null;
            return !string.IsNullOrEmpty(result.AnonymousTrackingCode) ? result : null;
        }
        return userId.HasValue && result.UserId == userId.Value ? result : null;
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(int chatSessionId)
    {
        var messages = await _messageRepo.GetAllAsync();
        return messages
            .Where(m => m.ChatSessionId == chatSessionId)
            .OrderBy(m => m.ChatMessageId)
            .Select(m => new ChatMessageDto(m.ChatMessageId, m.Role, m.Content, m.CitedJson))
            .ToList();
    }

    public async Task AppendMessageAsync(int chatSessionId, string role, string content, string? citedJson)
    {
        var session = await _sessionRepo.GetByIdAsync(chatSessionId)
            ?? throw new ArgumentException("Session not found", nameof(chatSessionId));
        if (session.Status == ChatSessionStatus.Committed)
            throw new InvalidOperationException("Committed chats are read-only.");
        await _messageRepo.AddAsync(new ChatMessage
        {
            ChatSessionId = chatSessionId, Role = role, Content = content,
            CitedJson = citedJson, CreatedAt = DateTime.UtcNow
        });
        await _messageRepo.SaveChangesAsync();
        session.UpdatedAt = DateTime.UtcNow;
        if (session.Title == "নতুন আলোচনা" && role == "user") session.Title = BuildTitle(content);
        await _sessionRepo.SaveChangesAsync();
    }

    // ---------- conversational turn loop (spec 3.1) ----------

    public async Task<ChatTurnDto> AskAsync(
        int chatSessionId,
        string question,
        string language,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question required", nameof(question));

        var session = await _sessionRepo.GetByIdAsync(chatSessionId)
                      ?? throw new ArgumentException("Session not found", nameof(chatSessionId));

        if (session.Status == ChatSessionStatus.Committed)
            throw new InvalidOperationException("Committed chats are read-only.");

        // 3-strike lock: this session is closed. Canned refusal, zero model
        // calls, free turn (controller treats Blocked=true as free).
        if (session.Status == ChatSessionStatus.Blocked)
        {
            var locked = CannedReply("locked", language);
            await _aiLogService.LogAsync(null, AiRequestType.ChatIntake,
                question, "[locked] " + locked, IntakeModelName, 1, 0, ct);
            return new ChatTurnDto(locked, Array.Empty<CitedSectionDto>(), DisclaimersFor(language),
                FromCache: false, RetrievalOnly: false, Tier: "full",
                Blocked: true, CaseFileJson: session.CaseFileJson);
        }

        // Layer 1 — free heuristic pre-filter: canned reply, no model call,
        // no quota charge, AI_LOG flag (spec 3.4).
        if (_safetyFilter.IsBlocked(question, out var flag))
            return await BlockedTurnAsync(session, "blocked", language, question, $"[safety:{flag}] ", ct);

        // A1: context-aware cache-first. Key = normalized question + THIS
        // session's case-file description + language, so a repeat question is
        // only served from cache when the case context matches — never another
        // conversation's personalized answer (spec 3.3 keeps the explain cache
        // case-file-keyed; this is the same discipline extended to full turns).
        // Only explanation-bearing entries (nonempty CitedJson) are served, so
        // gathering-phase turns can never collide. Hit = no model call, no
        // quota row, no state mutation — the turn is free (controller releases
        // its reservation on FromCache).
        var cachedTurn = await FindTurnCacheAsync(session.CaseFileJson, question, language);
        if (cachedTurn != null)
        {
            await _aiLogService.LogAsync(null, AiRequestType.ChatIntake,
                question, "[cache-hit] " + cachedTurn.Value.answer, IntakeModelName, 1, 0, ct);
            return new ChatTurnDto(cachedTurn.Value.answer, cachedTurn.Value.cited,
                DisclaimersFor(language), FromCache: true, RetrievalOnly: false, Tier: "full",
                Blocked: false, CaseFileJson: session.CaseFileJson);
        }

        // Structured case-file injection (spec 3.2): case file + last 3 raw
        // turns keep prompt tokens ~constant regardless of conversation length.
        var caseFileJson = session.CaseFileJson ?? "{}";
        var messages = await GetMessagesAsync(chatSessionId);
        var recentTurns = string.Join("\n", messages.TakeLast(3)
            .Select(m => (m.Role == "user" ? "Citizen: " : "Assistant: ") + m.Content));

        var prompt = PromptTemplates.ConversationalIntake
            .Replace("{caseFile}", caseFileJson)
            .Replace("{recentTurns}", string.IsNullOrWhiteSpace(recentTurns) ? "(none)" : recentTurns)
            .Replace("{message}", question)
            .Replace("{language}", language == "en" ? "en" : "bn");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var raw = await _aiService.GenerateContentAsync(prompt, ct);

        // Malformed JSON → one structured retry → still bad → plain-prose
        // turn, no state update (spec 7).
        var envelope = ParseEnvelope(raw);
        if (envelope == null)
        {
            var retryPrompt = prompt +
                "\n\nYour previous response was not valid JSON. Respond ONLY with the JSON envelope.";
            envelope = ParseEnvelope(await _aiService.GenerateContentAsync(retryPrompt, ct))
                       ?? new ChatEnvelope("normal", raw ?? string.Empty, null, Array.Empty<string>(), false, null, null, false);
        }
        sw.Stop();

        var intent = (envelope.Intent ?? "normal").Trim().ToLowerInvariant();
        if (intent != "normal")
            return await BlockedTurnAsync(session, intent, language, question, $"[intent:{intent}] ", ct);

        // Normal turn — reset any blocked streak.
        if (session.BlockedStreak != 0)
        {
            session.BlockedStreak = 0;
            session.UpdatedAt = DateTime.UtcNow;
            await _sessionRepo.SaveChangesAsync();
        }
        var reply = envelope.Reply ?? string.Empty;

        // Persist case file — full re-emit, last write wins (spec 3.2).
        // readyToExplain is ignored when intent != normal (spec 3.4 hard rule).
        if (!string.IsNullOrWhiteSpace(envelope.CaseFileJson))
        {
            session.CaseFileJson = envelope.CaseFileJson;
            session.Language = envelope.Language == "en" ? "en" : "bn";
            session.UpdatedAt = DateTime.UtcNow;
            await _sessionRepo.SaveChangesAsync();
        }

        var cited = new List<CitedSectionDto>();
        if (envelope.ReadyToExplain)
        {
            var cfJson = session.CaseFileJson ?? envelope.CaseFileJson ?? "{}";
            string explanationText;
            try
            {
                var explanation = await ExplainFromCaseFileAsync(cfJson, language, ct);
                explanationText = explanation.Explanation;
                cited = explanation.CitedSections.ToList();
            }
            catch
            {
                // Tier 2 — retrieval-only answer on explain-turn failure (spec 3.3)
                var fallback = await BuildRetrievalOnlyAnswerAsync(CaseFileToDescription(cfJson), language);
                explanationText = fallback.Answer;
                cited = fallback.CitedSections.ToList();
            }
            reply = string.Join("\n\n", reply, explanationText);

            // A1: cache the completed explanation turn (question + case file +
            // language key) so an identical repeat is free. Retrieval-only
            // fallbacks are NOT cached — they're degraded answers.
            if (cited.Count > 0)
            {
                await _cacheRepo.AddAsync(new AnswerCache
                {
                    QueryHash = TurnCacheKey(session.CaseFileJson ?? cfJson, question, language),
                    Question = question.Length > 480 ? question[..480] : question,
                    Answer = reply,
                    CitedJson = BuildCitedJson(cited),
                    HitCount = 0,
                    CreatedAt = DateTime.UtcNow
                });
                await _cacheRepo.SaveChangesAsync();
            }
        }

        // A model call happened → charge one quota turn via AI_LOG. RightsExplanation
        // + CaseId=null is exactly what AiBudgetService counts (spec 6). The
        // readyToExplain explain turn (if any) logged its own row inside the pipeline.
        var tokens = Math.Max(1, (prompt.Length + reply.Length) / 4);
        await _aiLogService.LogAsync(null, AiRequestType.RightsExplanation,
            prompt, reply, IntakeModelName, tokens, (int)sw.ElapsedMilliseconds, ct);

        var activeCaseFileJson = session.CaseFileJson ?? envelope.CaseFileJson;
        var hasDistrict = !string.IsNullOrWhiteSpace(CaseFileString(activeCaseFileJson, "district"));
        var hasCategory = envelope.SuggestedDraftType != null && MapCategory(envelope.SuggestedDraftType) != null;
        var noMissingInfo = envelope.MissingInfo == null || envelope.MissingInfo.Count == 0;

        var canDraft = envelope.CanDraft
            && envelope.ReadyToExplain
            && hasCategory
            && hasDistrict
            && noMissingInfo;

        return new ChatTurnDto(reply, cited, DisclaimersFor(language),
            FromCache: false, RetrievalOnly: false, Tier: "full",
            Blocked: false,
            SuggestedCategoryId: MapCategory(envelope.SuggestedDraftType),
            CaseFileJson: session.CaseFileJson,
            MissingInfo: envelope.MissingInfo,
            CanDraft: canDraft);
    }

    // Explain turn (spec 3.3): reuses the existing pipeline unchanged, keyed
    // off the case file instead of the raw transcript.
    private async Task<RightsExplanationDto> ExplainFromCaseFileAsync(
        string caseFileJson, string language, CancellationToken ct)
    {
        var description = CaseFileToDescription(caseFileJson);

        // ANSWER_CACHE re-keyed: hash of normalized case file + language.
        var hash = HashQuestion(NormalizeQuestion(description) + "|" + language);
        var cached = (await _cacheRepo.GetAllAsync()).FirstOrDefault(a => a.QueryHash == hash);
        if (cached != null)
        {
            cached.HitCount++;
            await _cacheRepo.SaveChangesAsync();
            return new RightsExplanationDto(cached.Answer, ParseCitedJson(cached.CitedJson),
                DisclaimersFor(language));
        }

        // Unsaved Case — CaseId = 0 keeps the pipeline's case-scoped writes inert.
        var chatCase = new Case
        {
            CaseId = 0,
            UserId = null,
            CategoryId = 1,
            DistrictId = 1,
            Title = "Chat",
            Description = description,
            Language = language == "en" ? "en" : "bn",
            Status = CaseStatus.Submitted,
            IsAnonymous = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await _rightsService.ExplainRightsAsync(chatCase, ct);

        await _cacheRepo.AddAsync(new AnswerCache
        {
            QueryHash = hash,
            Question = description.Length > 480 ? description[..480] : description,
            Answer = result.Explanation,
            CitedJson = BuildCitedJson(result.CitedSections),
            HitCount = 0,
            CreatedAt = DateTime.UtcNow
        });
        await _cacheRepo.SaveChangesAsync();
        return result;
    }

    // A2: standalone keyword section search (FR-7 from the chat home). No
    // model call, no quota — pure keyword/scenario retrieval. Same body as the
    // Tier-2 fallback, exposed for the "ধারা খুঁজুন" composer mode.
    public async Task<ChatTurnDto> SearchSectionsAsync(string question, string language)
        => await BuildRetrievalOnlyAnswerAsync(question, language);

    private async Task<ChatTurnDto> BuildRetrievalOnlyAnswerAsync(string question, string language)
    {
        var mappings = await _scenarioRepo.GetAllAsync();
        var q = question.Trim();
        var hits = mappings
            .Where(m => !string.IsNullOrWhiteSpace(m.ScenarioKeyword)
                        && q.Contains(m.ScenarioKeyword, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        var sections = new List<CitedSectionDto>();
        foreach (var m in hits)
        {
            var found = await _keywordSearch.SearchAsync(m.ScenarioKeyword, 2);
            foreach (var r in found)
            {
                if (sections.All(s => s.SectionId != r.SectionId))
                {
                    sections.Add(new CitedSectionDto(
                        r.SectionId,
                        r.ActTitle,
                        r.SectionNumber,
                        r.SectionText,
                        r.RelevanceScore,
                        r.Method.ToString(),
                        r.ActNumber,
                        r.ActYear));
                }
            }
            if (sections.Count >= 5) break;
        }
        if (sections.Count == 0)
        {
            var words = string.Join(" ", q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5));
            if (!string.IsNullOrWhiteSpace(words))
            {
                var found = await _keywordSearch.SearchAsync(words, 5);
                foreach (var r in found)
                {
                    if (sections.All(s => s.SectionId != r.SectionId))
                    {
                        sections.Add(new CitedSectionDto(
                            r.SectionId,
                            r.ActTitle,
                            r.SectionNumber,
                            r.SectionText,
                            r.RelevanceScore,
                            r.Method.ToString(),
                            r.ActNumber,
                            r.ActYear));
                    }
                }
            }
        }

        var header = language == "en"
            ? "AI is unavailable right now. Relevant statutory sections found by keyword search:"
            : "AI এই মুহূর্তে উপলব্ধ নয়। কীওয়ার্ড অনুসন্ধানে প্রাপ্ত প্রাসঙ্গিক ধারাসমূহ:";
        var body = new StringBuilder(header).Append('\n');
        foreach (var s in sections)
        {
            body.Append("• ").Append(s.ActTitle);
            if (!string.IsNullOrWhiteSpace(s.SectionNumber)) body.Append(" — ধারা ").Append(s.SectionNumber);
            body.Append('\n');
            body.Append(s.SectionText.Length > 300 ? s.SectionText[..300] + "…" : s.SectionText);
            body.Append("\n\n");
        }
        if (sections.Count == 0)
        {
            body.Clear().Append(language == "en"
                ? "No relevant sections found. Please try different keywords."
                : "প্রাসঙ্গিক কোনো ধারা পাওয়া যায়নি। ভিন্ন শব্দ দিয়ে চেষ্টা করুন।");
        }

        return new ChatTurnDto(body.ToString(), sections, DisclaimersFor(language),
            FromCache: false, RetrievalOnly: true, Tier: "retrieval-only");
    }

    // ---------- commit ([Generate Draft] confirm card, spec 3.5) ----------

    public async Task<ChatCommitResultDto> CommitToCaseAsync(
        int chatSessionId,
        int categoryId,
        byte districtId,
        string? title,
        string? notificationEmail,
        bool isAnonymous,
        int? userId,
        string? language = null,
        CancellationToken ct = default)
    {
        var session = await _sessionRepo.GetByIdAsync(chatSessionId)
                      ?? throw new ArgumentException("Session not found", nameof(chatSessionId));

        if (session.Status == ChatSessionStatus.Committed)
        {
            if (!session.CommittedCaseId.HasValue)
                throw new InvalidOperationException("Committed case is unavailable.");
            var existingCase = await _caseRepoTyped.GetWithDocumentsAsync(session.CommittedCaseId.Value);
            if (existingCase == null || existingCase.CaseId != session.CommittedCaseId.Value)
                throw new InvalidOperationException("Committed case is unavailable.");
            var existingDoc = existingCase.Documents.OrderBy(d => d.DocumentId).LastOrDefault();
            if (existingDoc == null)
                throw new InvalidOperationException("Committed document is unavailable.");
            return new ChatCommitResultDto(existingCase.CaseId, existingCase.AnonymousTrackingCode,
                existingDoc.DocumentId, existingDoc.ContentDraft);
        }

        var messages = await GetMessagesAsync(chatSessionId);
        if (messages.Count == 0)
            throw new InvalidOperationException("Cannot commit an empty conversation");

        // Conversational collection: the case file is the source of truth when
        // present (chat path); the form path has no case file → transcript.
        var caseFileJson = session.CaseFileJson;
        var unifiedDescription = caseFileJson != null
            ? CaseFileToDescription(caseFileJson)
            : BuildTranscript(messages);

        // Missing confirm-card fields come from the case file (spec 3.5);
        // explicit request-body values (form path) still win.
        if (categoryId <= 0)
            categoryId = MapCategory(CaseFileString(caseFileJson, "category")) ?? 0;
        if (districtId <= 0)
            districtId = await ResolveDistrictIdAsync(CaseFileString(caseFileJson, "district"));
        if (string.IsNullOrWhiteSpace(title))
            title = CaseFileString(caseFileJson, "title") ?? session.Title;
        language ??= session.Language;

        if (categoryId <= 0 || districtId <= 0)
            throw new InvalidOperationException(
                "আলোচনায় জেলা ও বিভাগ জানালে এগোনো যাবে। / Tell the assistant your district and issue type in the chat to continue.");

        string? trackingCode = isAnonymous || userId == null
            ? Guid.NewGuid().ToString("N")
            : null;

        var caseEntity = new Case
        {
            UserId = isAnonymous ? null : userId,
            CategoryId = categoryId,
            DistrictId = districtId,
            Title = _encryptionService.Encrypt(title),
            Description = _encryptionService.Encrypt(unifiedDescription),
            Language = language == "en" ? "en" : "bn",
            Status = CaseStatus.Submitted,
            IsAnonymous = isAnonymous,
            AnonymousTrackingCode = trackingCode,
            NotificationEmail = string.IsNullOrWhiteSpace(notificationEmail) ? null : notificationEmail.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _caseRepo.AddAsync(caseEntity);
        await _caseRepo.SaveChangesAsync();
        var encryptedTitle = caseEntity.Title;
        var encryptedDescription = caseEntity.Description;

        // Case-critical generation (NOT metered as a chat turn — marker absent).
        // ExplainRightsAsync and the document templates (e.g. RtiRequestTemplate
        // embeds Description verbatim into the letter body) need PLAINTEXT, so
        // the same tracked entity is temporarily set back to plaintext here.
        // caseEntity/loaded/DocumentService's own lookup all resolve to the
        // SAME EF-tracked instance (identity map, shared scoped DbContext), so
        // this plaintext WOULD otherwise get persisted as-is by
        // GenerateDocumentAsync's internal SaveChangesAsync -- silently
        // defeating the field-level PII encryption on every committed case.
        // Restore + re-save the ciphertext immediately after, so the DB never
        // keeps Title/Description in plaintext once this method returns.
        var loaded = await _caseRepoTyped.GetWithDocumentsAsync(caseEntity.CaseId) ?? caseEntity;
        loaded.Title = title;
        loaded.Description = unifiedDescription;

        var explanation = await _rightsService.ExplainRightsAsync(loaded, ct);
        var doc = await _documentService.GenerateDocumentAsync(caseEntity.CaseId, explanation);

        loaded.Title = encryptedTitle;
        loaded.Description = encryptedDescription;
        await _caseRepo.SaveChangesAsync();

        session.Status = ChatSessionStatus.Committed;
        session.CommittedCaseId = caseEntity.CaseId;
        session.UpdatedAt = DateTime.UtcNow;
        await _sessionRepo.SaveChangesAsync();

        if (userId.HasValue && !isAnonymous)
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

        return new ChatCommitResultDto(caseEntity.CaseId, trackingCode, doc.DocumentId, doc.ContentDraft);
    }

    private static string BuildTranscript(IReadOnlyList<ChatMessageDto> messages)
    {
        var sb = new StringBuilder();
        foreach (var m in messages)
        {
            sb.Append(m.Role == "user" ? "নাগরিক: " : "সহায়ক: ").Append(m.Content).Append("\n\n");
        }
        return sb.ToString().Trim();
    }

    // ---------- case-file helpers (open schema — C# stores it opaquely) ----------

    // Envelope "suggestedDraftType" / case-file "category" value → DB category.
    internal static readonly Dictionary<string, int> CategoryByDraftType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["LabourComplaint"] = 1,
            ["GeneralDiary"] = 2,
            ["RtiRequest"] = 3,
            ["ConsumerComplaint"] = 4
        };

    public static int? MapCategory(string? draftType)
        => draftType != null && CategoryByDraftType.TryGetValue(draftType.Trim(), out var id) ? id : null;

    private async Task<byte> ResolveDistrictIdAsync(string? districtValue)
        => MatchDistrictId(districtValue, await _districtRepo.GetAllAsync());

    // Older/alternate English spellings the model emits → the official spelling
    // stored in DISTRICT.Name (both sides compared via NormalizeDistrict).
    private static readonly Dictionary<string, string> DistrictAliases = new()
    {
        ["chittagong"] = "chattogram",
        ["comilla"] = "cumilla",
        ["barisal"] = "barishal",
        ["bogra"] = "bogura",
        ["jessore"] = "jashore",
        ["jhalakathi"] = "jhalokathi",
        ["jhalokati"] = "jhalokathi",
        ["maulvibazar"] = "moulvibazar",
        ["netrakona"] = "netrokona",
        ["khagrachari"] = "khagrachhari",
        ["laxmipur"] = "lakshmipur",
        ["coxbazar"] = "coxsbazar",
        ["habigonj"] = "habiganj",
        ["jaipurhat"] = "joypurhat",
    };

    // Letters only, lowercased, without a trailing "district" — so "Cox's Bazar",
    // "cox bazar" and "Chittagong District" all compare cleanly.
    private static string NormalizeDistrict(string value)
    {
        var n = new string(value.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return n.EndsWith("district") ? n[..^"district".Length] : n;
    }

    public static byte MatchDistrictId(string? districtValue, IEnumerable<District> districts)
    {
        if (string.IsNullOrWhiteSpace(districtValue)) return 0;
        if (byte.TryParse(districtValue.Trim(), out var id)) return id;

        var wanted = NormalizeDistrict(districtValue);
        if (DistrictAliases.TryGetValue(wanted, out var official)) wanted = official;
        return districts.FirstOrDefault(d => NormalizeDistrict(d.Name) == wanted)?.DistrictId ?? 0;
    }

    // Reads one top-level string slot from the case-file JSON, case-insensitively.
    public static string? CaseFileString(string? caseFileJson, string key)
    {
        if (string.IsNullOrWhiteSpace(caseFileJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(caseFileJson);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    // Flattens the open-schema case file into "key: value" lines for the RAG
    // embed query, explain prompt, stored Case.Description, and cache key.
    public static string CaseFileToDescription(string? caseFileJson)
    {
        if (string.IsNullOrWhiteSpace(caseFileJson)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(caseFileJson);
            var sb = new StringBuilder();
            FlattenCaseFile(string.Empty, doc.RootElement, sb);
            return sb.ToString().Trim();
        }
        catch
        {
            return caseFileJson;
        }
    }

    private static void FlattenCaseFile(string prefix, JsonElement el, StringBuilder sb)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                    FlattenCaseFile(prefix.Length > 0 ? prefix + "." + p.Name : p.Name, p.Value, sb);
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    FlattenCaseFile(prefix, item, sb);
                break;
            default:
                if (el.ValueKind != JsonValueKind.Null)
                    sb.Append(prefix).Append(": ").Append(el.ToString()).Append('\n');
                break;
        }
    }

    // ---------- envelope parsing ----------

    public static ChatEnvelope? ParseEnvelope(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(StripFences(raw));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? reply = null, intent = null, caseFile = null, draftType = null, lang = null;
            bool canDraft = false;
            var missing = new List<string>();
            foreach (var p in root.EnumerateObject())
            {
                switch (p.Name.ToLowerInvariant())
                {
                    case "reply" when p.Value.ValueKind == JsonValueKind.String: reply = p.Value.GetString(); break;
                    case "intent" when p.Value.ValueKind == JsonValueKind.String: intent = p.Value.GetString(); break;
                    case "casefile" when p.Value.ValueKind == JsonValueKind.Object: caseFile = p.Value.GetRawText(); break;
                    case "suggesteddrafttype" when p.Value.ValueKind == JsonValueKind.String:
                        draftType = p.Value.GetString(); break;
                    case "language" when p.Value.ValueKind == JsonValueKind.String: lang = p.Value.GetString(); break;
                    case "candraft":
                        canDraft = p.Value.ValueKind == JsonValueKind.True ||
                                   (p.Value.ValueKind == JsonValueKind.String && bool.TryParse(p.Value.GetString(), out var parsed) && parsed);
                        break;
                    case "missinginfo" when p.Value.ValueKind == JsonValueKind.Array:
                        missing.AddRange(p.Value.EnumerateArray()
                            .Where(e => e.ValueKind == JsonValueKind.String)
                            .Select(e => e.GetString()!));
                        break;
                }
            }
            var ready = root.TryGetProperty("readyToExplain", out var rt) && rt.ValueKind == JsonValueKind.True;
            return new ChatEnvelope(intent ?? "normal", reply ?? string.Empty, caseFile,
                missing, ready, draftType, lang, canDraft);
        }
        catch
        {
            return null;
        }
    }

    private static string StripFences(string raw)
    {
        var t = raw.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal)) return t;
        var firstNewline = t.IndexOf('\n');
        if (firstNewline < 0) return t;
        t = t[(firstNewline + 1)..];
        var lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0) t = t[..lastFence];
        return t.Trim();
    }

    // ---------- canned boundary replies (spec 3.4) ----------

    // Refuse + escalate: every blocked turn bumps the streak; at the threshold
    // the session is locked. Locked sessions are excluded from resume lookup,
    // so the citizen naturally starts a fresh chat. All attempts are AI_LOGged.
    private async Task<ChatTurnDto> BlockedTurnAsync(
        ChatSession session, string intent, string language, string question,
        string logPrefix, CancellationToken ct)
    {
        session.BlockedStreak++;
        if (session.BlockedStreak >= BlockedStreakLockThreshold)
            session.Status = ChatSessionStatus.Blocked;
        session.UpdatedAt = DateTime.UtcNow;
        await _sessionRepo.SaveChangesAsync();

        var canned = CannedReply(
            session.Status == ChatSessionStatus.Blocked ? "locked" : intent, language);
        await _aiLogService.LogAsync(null, AiRequestType.ChatIntake,
            question, $"{logPrefix}{canned}", IntakeModelName, 1, 0, ct);
        return new ChatTurnDto(canned, Array.Empty<CitedSectionDto>(), DisclaimersFor(language),
            FromCache: false, RetrievalOnly: false, Tier: "full",
            Blocked: true, CaseFileJson: session.CaseFileJson);
    }

    private static string CannedReply(string intent, string language)
    {
        var en = language == "en";
        return intent switch
        {
            "locked" => en
                ? "This conversation has been closed because of repeated rule violations. Please start a new chat and describe a genuine legal problem."
                : "বারবার নিয়ম ভাঙার কারণে এই আলোচনাটি বন্ধ করা হয়েছে। অনুগ্রহ করে নতুন আলোচনা শুরু করে আপনার আসল আইনি সমস্যাটি বলুন।",
            "probing" or "injection" => en
                ? "I can't share my internal instructions. I'm here to help you understand your legal rights in Bangladesh — tell me what happened."
                : "আমি আমার অভ্যন্তরীণ নির্দেশনা শেয়ার করতে পারি না। আমি বাংলাদেশে আপনার আইনি অধিকার বুঝতে সাহায্য করি — কী ঘটেছে তা বলুন।",
            _ => en
                ? "I only help with legal problems. Describe the legal issue you're facing and I'll gather the facts and explain your rights."
                : "আমি শুধু আইনি সমস্যা নিয়ে সাহায্য করি। আপনার আইনি সমস্যাটি বলুন — তথ্য নিয়ে আপনার অধিকার ব্যাখ্যা করব।"
        };
    }

    // ---------- helpers ----------

    // A1: SHA-256 of normalized(question) + case-file description + language.
    private static string TurnCacheKey(string? caseFileJson, string question, string language)
        => HashQuestion(NormalizeQuestion(question) + "|" + CaseFileToDescription(caseFileJson) + "|" + language);

    // A1: cache-hit lookup for a repeat question against THIS session's case
    // context. Explanation-bearing entries only (gathering turns mutate the
    // case file, so serving them from cache would skip a state update).
    private async Task<(string answer, IReadOnlyList<CitedSectionDto> cited)?> FindTurnCacheAsync(
        string? caseFileJson, string question, string language)
    {
        if (string.IsNullOrWhiteSpace(caseFileJson)) return null;

        var hash = TurnCacheKey(caseFileJson, question, language);
        var cached = (await _cacheRepo.GetAllAsync())
            .FirstOrDefault(a => a.QueryHash == hash && !string.IsNullOrWhiteSpace(a.CitedJson)
                                 && a.CitedJson != "[]");
        if (cached == null) return null;

        cached.HitCount++;
        await _cacheRepo.SaveChangesAsync();
        return (cached.Answer, ParseCitedJson(cached.CitedJson));
    }

    public static string NormalizeQuestion(string question)
    {
        var lowered = question.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        var s = sb.ToString();
        s = s.Replace('\u09E6', '0').Replace('\u09E7', '1').Replace('\u09E8', '2')
             .Replace('\u09E9', '3').Replace('\u09EA', '4').Replace('\u09EB', '5')
             .Replace('\u09EC', '6').Replace('\u09ED', '7').Replace('\u09EE', '8')
             .Replace('\u09EF', '9');
        return s;
    }

    private static string HashQuestion(string normalized)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));

    internal static string BuildCitedJson(IReadOnlyList<CitedSectionDto> sections)
    {
        var parts = sections.Select(s =>
            "{\"sectionId\":" + s.SectionId +
            ",\"actTitle\":\"" + EscapeJson(s.ActTitle) +
            "\",\"sectionNumber\":\"" + EscapeJson(s.SectionNumber) +
            "\",\"relevanceScore\":" + s.RelevanceScore.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
        return "[" + string.Join(",", parts) + "]";
    }

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    private static string DisclaimersFor(string language)
        => MuktoAin.Domain.Constants.Disclaimers.ForLanguage(language == "en" ? "en" : "bn");

    internal static IReadOnlyList<CitedSectionDto> ParseCitedJson(string? citedJson)
    {
        if (string.IsNullOrWhiteSpace(citedJson)) return new List<CitedSectionDto>();
        try
        {
            using var doc = JsonDocument.Parse(citedJson);
            var result = new List<CitedSectionDto>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                // relevanceScore is absent on rows cached before this field existed --
                // default to 0 rather than throw, so old cache entries keep working.
                var relevanceScore = el.TryGetProperty("relevanceScore", out var rs)
                    ? rs.GetSingle()
                    : 0f;
                result.Add(new CitedSectionDto(
                    el.GetProperty("sectionId").GetInt32(),
                    el.GetProperty("actTitle").GetString() ?? string.Empty,
                    el.GetProperty("sectionNumber").GetString() ?? string.Empty,
                    SectionText: string.Empty,
                    RelevanceScore: relevanceScore,
                    RetrievalMethod: "Cache",
                    ActNumber: string.Empty,
                    ActYear: 0));
            }
            return result;
        }
        catch
        {
            return new List<CitedSectionDto>();
        }
    }
}
