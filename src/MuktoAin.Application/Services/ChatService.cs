using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MuktoAin.Application.DTOs;
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
public class ChatService
{
    private const int MaxRecentChats = 8;

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
    private readonly IRepository<CaseCategory> _categoryRepo;

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
        IRepository<CaseCategory> categoryRepo)
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
        _categoryRepo = categoryRepo;
    }

    // ---------- session management ----------

    public async Task<ChatSession> GetOrCreateSessionAsync(int? userId, string? sessionKey, string? firstMessage)
    {
        var all = await _sessionRepo.GetAllAsync();
        ChatSession? existing = null;
        if (userId.HasValue)
        {
            existing = all.Where(s => s.UserId == userId
                                 && s.Status == ChatSessionStatus.InProgress)
                          .OrderByDescending(s => s.UpdatedAt)
                          .FirstOrDefault();
        }
        else if (!string.IsNullOrEmpty(sessionKey))
        {
            existing = all.Where(s => s.SessionKey == sessionKey
                                 && s.Status == ChatSessionStatus.InProgress)
                          .OrderByDescending(s => s.UpdatedAt)
                          .FirstOrDefault();
        }
        if (existing != null) return existing;

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

    public async Task<IReadOnlyList<RecentChatDto>> GetRecentAsync(int? userId, string? sessionKey)
    {
        var all = await _sessionRepo.GetAllAsync();
        IEnumerable<ChatSession> inProgress = all.Where(s => s.Status == ChatSessionStatus.InProgress);

        if (userId.HasValue)
            inProgress = inProgress.Where(s => s.UserId == userId);
        else if (!string.IsNullOrEmpty(sessionKey))
            inProgress = inProgress.Where(s => s.SessionKey == sessionKey);
        else
            return new List<RecentChatDto>();

        var recent = inProgress.OrderByDescending(s => s.UpdatedAt).Take(MaxRecentChats).ToList();
        var messages = await _messageRepo.GetAllAsync();
        var ids = recent.Select(s => s.ChatSessionId).ToHashSet();
        var counts = messages.Where(m => ids.Contains(m.ChatSessionId))
                             .GroupBy(m => m.ChatSessionId)
                             .ToDictionary(g => g.Key, g => g.Count());

        return recent.Select(s => new RecentChatDto(
            s.ChatSessionId,
            s.Title,
            s.UpdatedAt,
            counts.TryGetValue(s.ChatSessionId, out var c) ? c : 0)).ToList();
    }

    public async Task<ChatSession?> GetSessionAsync(int chatSessionId)
        => await _sessionRepo.GetByIdAsync(chatSessionId);

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
        await _messageRepo.AddAsync(new ChatMessage
        {
            ChatSessionId = chatSessionId,
            Role = role,
            Content = content,
            CitedJson = citedJson,
            CreatedAt = DateTime.UtcNow
        });
        await _messageRepo.SaveChangesAsync();

        var session = await _sessionRepo.GetByIdAsync(chatSessionId);
        if (session != null)
        {
            session.UpdatedAt = DateTime.UtcNow;
            if (session.Title == "নতুন আলোচনা" && role == "user")
                session.Title = BuildTitle(content);
            await _sessionRepo.SaveChangesAsync();
        }
    }

    // ---------- asking (quota ladder) ----------

    public async Task<ChatTurnDto> AskAsync(
        int chatSessionId,
        string question,
        string language,
        bool allowCapped,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question required", nameof(question));

        var session = await _sessionRepo.GetByIdAsync(chatSessionId)
                      ?? throw new ArgumentException("Session not found", nameof(chatSessionId));

        // Tier 0 — answer cache
        var hash = HashQuestion(NormalizeQuestion(question));
        var cached = (await _cacheRepo.GetAllAsync()).FirstOrDefault(a => a.QueryHash == hash);
        if (cached != null)
        {
            cached.HitCount++;
            await _cacheRepo.SaveChangesAsync();
            return new ChatTurnDto(cached.Answer, ParseCitedJson(cached.CitedJson),
                DisclaimersFor(language), FromCache: true, RetrievalOnly: false, Tier: "full");
        }

        // Unsaved Case — CaseId = 0 keeps the pipeline's case-scoped writes
        // inert. The question goes in RAW: the pipeline logs with CaseId =
        // null for unsaved cases, which is exactly what AiBudgetService
        // counts — no marker pollution of the embed query or prompt.
        var chatCase = new Case
        {
            CaseId = 0,
            UserId = session.UserId,
            CategoryId = 1,
            DistrictId = 1,
            Title = "Chat",
            Description = question,
            Language = language == "en" ? "en" : "bn",
            Status = CaseStatus.Submitted,
            IsAnonymous = session.UserId == null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        RightsExplanationDto explanation;
        try
        {
            explanation = await _rightsService.ExplainRightsAsync(chatCase, ct);
        }
        catch
        {
            // Tier 2 — retrieval-only answer (AI down / quota exhausted mid-flight)
            return await BuildRetrievalOnlyAnswerAsync(question, language);
        }

        await _cacheRepo.AddAsync(new AnswerCache
        {
            QueryHash = hash,
            Question = question.Length > 480 ? question[..480] : question,
            Answer = explanation.Explanation,
            CitedJson = BuildCitedJson(explanation.CitedSections),
            HitCount = 0,
            CreatedAt = DateTime.UtcNow
        });
        await _cacheRepo.SaveChangesAsync();

        return new ChatTurnDto(
            explanation.Explanation,
            explanation.CitedSections,
            explanation.Disclaimer,
            FromCache: false,
            RetrievalOnly: false,
            Tier: allowCapped ? "capped" : "full");
    }

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

    // ---------- category suggestion (Generate Draft modal default) ----------

    // CASE_CATEGORY.CommonActions/CommonActionsEn hold formal legal phrasing
    // ("Written claim for unpaid or delayed wages") that shares almost no
    // vocabulary with how citizens actually describe their problem in chat
    // ("my boss hasn't paid my salary") -- verified against a real turn, DB
    // words alone scored 0 for every category. So this scores WORD overlap
    // against the DB text PLUS a curated conversational synonym list per
    // category, and returns null (placeholder stays) when nothing scores.
    private static readonly Dictionary<int, string[]> CategorySynonyms = new()
    {
        [1] = new[] // Labour Complaint
        {
            "salary", "wage", "wages", "pay", "paid", "unpaid", "overtime", "bonus",
            "fired", "fire", "dismiss", "dismissed", "dismissal", "terminate", "terminated",
            "termination", "employer", "boss", "workplace", "factory", "worker", "employee",
            "job", "labour", "labor", "maternity", "injury", "accident", "harassment",
            "বেতন", "মজুরি", "চাকরি", "বরখাস্ত", "শ্রমিক", "মালিক", "কারখানা", "ছাঁটাই", "কর্মচারী"
        },
        [2] = new[] // General Diary (GD)
        {
            "lost", "stolen", "theft", "steal", "stole", "missing", "threat", "threaten",
            "threatened", "harassment", "police", "id", "nid", "passport", "certificate",
            "robbery", "robbed", "kidnap", "assault", "attacked", "diary",
            "হারিয়ে", "চুরি", "নিখোঁজ", "হুমকি", "থানা", "জিডি", "ছিনতাই"
        },
        [3] = new[] // RTI Request
        {
            "information", "rti", "government", "office", "budget", "records", "record",
            "documents", "document", "decision", "minutes", "report", "authority", "public",
            "তথ্য", "অধিকার", "সরকারি", "দপ্তর", "বাজেট", "প্রতিবেদন"
        },
        [4] = new[] // Consumer Complaint
        {
            "product", "price", "refund", "overcharge", "overcharged", "expired", "fake",
            "counterfeit", "fraud", "shop", "shopkeeper", "warranty", "defective", "quality",
            "receipt", "purchase", "bought", "পণ্য", "মূল্য", "দোকান", "ভেজাল", "প্রতারণা", "দাম"
        }
    };

    public async Task<int?> SuggestCategoryAsync(int chatSessionId)
    {
        var messages = await GetMessagesAsync(chatSessionId);
        var text = string.Join(" ", messages.Where(m => m.Role == "user").Select(m => m.Content));
        var textWords = ExtractWords(text);
        if (textWords.Count == 0) return null;

        var categories = await _categoryRepo.GetAllAsync();
        int? bestCategoryId = null;
        var bestScore = 0;
        foreach (var cat in categories)
        {
            var catWords = ExtractWords(string.Join(" ", cat.Name, cat.NameBn, cat.CommonActions, cat.CommonActionsEn));
            if (CategorySynonyms.TryGetValue(cat.CategoryId, out var synonyms))
                catWords.UnionWith(synonyms);

            var score = textWords.Count(w => catWords.Contains(w));
            if (score > bestScore)
            {
                bestScore = score;
                bestCategoryId = cat.CategoryId;
            }
        }
        return bestScore > 0 ? bestCategoryId : null;
    }

    private static readonly HashSet<string> CategoryStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "for", "and", "or", "of", "to", "in", "on", "at", "is", "are",
        "was", "were", "this", "that", "with", "from", "by"
    };

    // Unicode letter+mark runs (2+ chars), minus English stopwords -- \p{M} is
    // required alongside \p{L} because Bangla vowel signs/virama (matras, e.g.
    // the ে in বেতন) are combining MARKS, not letters: [\p{L}]+ alone splits
    // বেতন into "ব"+"তন" and never matches the whole word. Works across
    // Bangla/English/Banglish without a language-specific tokenizer.
    private static HashSet<string> ExtractWords(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Regex.Matches(s, @"[\p{L}\p{M}]{2,}")
            .Select(m => m.Value)
            .Where(w => !CategoryStopWords.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ---------- commit ([Generate Draft]) ----------

    public async Task<ChatCommitResultDto> CommitToCaseAsync(
        int chatSessionId,
        int categoryId,
        byte districtId,
        string title,
        string? notificationEmail,
        bool isAnonymous,
        int? userId,
        string? language = null,
        CancellationToken ct = default)
    {
        var session = await _sessionRepo.GetByIdAsync(chatSessionId)
                      ?? throw new ArgumentException("Session not found", nameof(chatSessionId));

        var messages = await GetMessagesAsync(chatSessionId);
        if (messages.Count == 0)
            throw new InvalidOperationException("Cannot commit an empty conversation");

        // Unified description = transcript (the form path writes its answers
        // into a chat session the same way — one transcript per case, always)
        var sb = new StringBuilder();
        foreach (var m in messages)
        {
            sb.Append(m.Role == "user" ? "নাগরিক: " : "সহায়ক: ").Append(m.Content).Append("\n\n");
        }
        var unifiedDescription = sb.ToString().Trim();

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

        return new ChatCommitResultDto(caseEntity.CaseId, trackingCode, doc.DocumentId, doc.ContentDraft);
    }

    // ---------- helpers ----------

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
            using var doc = System.Text.Json.JsonDocument.Parse(citedJson);
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
