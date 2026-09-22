using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using MuktoAin.Web.Controllers;
using MuktoAin.Web.Session;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class ChatControllerTests
{
    private readonly Mock<IRepository<ChatSession>> _sessionRepo = new();
    private readonly ChatController _controller;
    private readonly Mock<IAiTurnReservationStore> _store = DefaultReservationStore();

    private static Mock<IAiTurnReservationStore> DefaultReservationStore()
    {
        var store = new Mock<IAiTurnReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TurnReservation(1, PaidWithCredit: false));
        return store;
    }

    public ChatControllerTests()
    {
        var chatService = new ChatService(
            _sessionRepo.Object,
            Mock.Of<IRepository<ChatMessage>>(),
            Mock.Of<IRepository<Case>>(),
            Mock.Of<ICaseRepository>(),
            Mock.Of<IRepository<AnswerCache>>(),
            Mock.Of<IRightsExplanationService>(),
            null!,
            Mock.Of<IEncryptionService>(),
            Mock.Of<IScenarioMappingRepository>(),
            Mock.Of<MuktoAin.Domain.Interfaces.Services.IKeywordSectionSearch>(),
            Mock.Of<IRepository<District>>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IAiService>(),
            Mock.Of<IAiLogService>(),
            Mock.Of<IChatHistoryRepository>(),
            Mock.Of<IRepository<Notification>>());

        var budgetService = new AiBudgetService(_store.Object);

        var httpContext = new DefaultHttpContext
        {
            // SessionKey() reads HttpContext.Session during the ownership check,
            // so the fixture needs a session-backed context (not the featureless
            // DefaultHttpContext default, which throws "Session has not been
            // configured").
            Session = new TestSession(),
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "42")
            }))
        };

        _controller = new ChatController(chatService, budgetService, Mock.Of<IActSectionRepository>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedReplay_RemembersAnonymousCodeServerSideOnly(bool adopted)
    {
        var session = new ChatSession
        {
            ChatSessionId = 15, SessionKey = "synthetic-key", UserId = adopted ? 42 : null,
            Status = ChatSessionStatus.Committed, CommittedCaseId = 90
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);
        var cases = new Mock<ICaseRepository>();
        cases.Setup(r => r.GetWithDocumentsAsync(90)).ReturnsAsync(new Case
            { CaseId = 90, IsAnonymous = true, AnonymousTrackingCode = "synthetic-private-code" });
        var messages = new Mock<IRepository<ChatMessage>>();
        messages.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
            { new ChatMessage { ChatSessionId = 15, Role = "user", Content = "Synthetic" } });
        var service = new ChatService(_sessionRepo.Object, messages.Object,
            Mock.Of<IRepository<Case>>(), cases.Object, Mock.Of<IRepository<AnswerCache>>(),
            Mock.Of<IRightsExplanationService>(), null!, Mock.Of<IEncryptionService>(),
            Mock.Of<IScenarioMappingRepository>(), Mock.Of<IKeywordSectionSearch>(),
            Mock.Of<IRepository<District>>(), Mock.Of<IAiService>(), Mock.Of<IAiLogService>(),
            Mock.Of<IChatHistoryRepository>(),
            Mock.Of<IRepository<Notification>>());
        var controller = new ChatController(service,
            new AiBudgetService(DefaultReservationStore().Object),
            Mock.Of<IActSectionRepository>()) { ControllerContext = _controller.ControllerContext };
        if (!adopted) controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        controller.HttpContext.Session.SetString("mkt-chatkey", "synthetic-key");
        var url = new Mock<IUrlHelper>();
        url.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>()))
            .Returns<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>(c =>
            {
                Assert.Null(c.Values!.GetType().GetProperty("code"));
                return "/Case/Result?id=90";
            });
        controller.Url = url.Object;
        var result = Assert.IsType<JsonResult>(await controller.Messages(15));
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("synthetic-private-code", json);
        Assert.DoesNotContain("trackingCode", json);
        Assert.Equal("synthetic-private-code", TrackedCases.Resolve(controller.HttpContext.Session, 90));
        Assert.Equal(false, result.Value!.GetType().GetProperty("canDraft")!.GetValue(result.Value));
        Assert.Equal("/Case/Result?id=90", result.Value.GetType().GetProperty("caseUrl")!.GetValue(result.Value));
    }

    [Theory]
    [InlineData("rights")]
    [InlineData("search")]
    public async Task Committed_AskReturns409BeforeQuota(string mode)
    {
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(new ChatSession
            { ChatSessionId = 15, UserId = 42, Status = ChatSessionStatus.Committed });
        var result = await _controller.Ask(new ChatAskRequest
            { ChatSessionId = 15, Question = "Synthetic question", Mode = mode });
        Assert.IsType<ConflictObjectResult>(result);
        _store.Verify(s => s.TryReserveAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ChatSessionStatus.InProgress)]
    [InlineData(ChatSessionStatus.Committed)]
    public async Task Delete_OwnedSession_RemovesItAndSaves(ChatSessionStatus status)
    {
        var session = new ChatSession { ChatSessionId = 15, UserId = 42, Status = status };
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);
        var result = await _controller.Delete(new ChatDeleteRequest { ChatSessionId = 15 });
        Assert.IsType<JsonResult>(result);
        _sessionRepo.Verify(r => r.DeleteAsync(session), Times.Once);
        _sessionRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Delete_ForeignGuestOrMissingSession_DeletesNothing()
    {
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(new ChatSession
            { ChatSessionId = 15, UserId = 7 });
        _sessionRepo.Setup(r => r.GetByIdAsync(16)).ReturnsAsync(new ChatSession
            { ChatSessionId = 16, SessionKey = "guest-b" });
        Assert.IsType<ForbidResult>(await _controller.Delete(new ChatDeleteRequest { ChatSessionId = 15 }));
        _controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.HttpContext.Session.SetString("mkt-chatkey", "guest-a");
        Assert.IsType<ForbidResult>(await _controller.Delete(new ChatDeleteRequest { ChatSessionId = 16 }));
        Assert.IsType<NotFoundObjectResult>(await _controller.Delete(new ChatDeleteRequest { ChatSessionId = 99 }));
        Assert.IsType<BadRequestObjectResult>(await _controller.Delete(new ChatDeleteRequest { ChatSessionId = 0 }));
        Assert.IsType<BadRequestObjectResult>(await _controller.Delete(null));
        _sessionRepo.Verify(r => r.DeleteAsync(It.IsAny<ChatSession>()), Times.Never);
        _sessionRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuestOwnership_RejectsUnrelatedOrAdoptedSession(bool adopted)
    {
        _controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.HttpContext.Session.SetString("mkt-chatkey", "guest-a");
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(new ChatSession
            { ChatSessionId = 15, UserId = adopted ? 42 : null, SessionKey = adopted ? "guest-a" : "guest-b" });
        Assert.IsType<ForbidResult>(await _controller.Messages(15));
        Assert.IsType<ForbidResult>(await _controller.Ask(new ChatAskRequest
            { ChatSessionId = 15, Question = "Synthetic question" }));
        Assert.IsType<ForbidResult>(await _controller.Commit(new ChatCommitRequest { ChatSessionId = 15 }));
    }

    [Fact]
    public async Task NewChat_RequestCreatesFreshSession()
    {
        _sessionRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new ChatSession { ChatSessionId = 15, UserId = 42, Title = "Existing" }
        });
        var result = Assert.IsType<JsonResult>(await _controller.New(new ChatNewRequest
            { FirstMessage = "Fresh message", NewChat = true }));
        Assert.Equal("Fresh message", result.Value!.GetType().GetProperty("title")!.GetValue(result.Value));
        _sessionRepo.Verify(r => r.GetAllAsync(), Times.Never);
        _sessionRepo.Verify(r => r.AddAsync(It.Is<ChatSession>(s => s.UserId == 42 && s.Title == "Fresh message")), Times.Once);
    }

    [Fact]
    public async Task Recent_RejectsPartialCursor()
    {
        Assert.IsType<BadRequestObjectResult>(await _controller.Recent(DateTime.UtcNow, null));
        Assert.IsType<BadRequestObjectResult>(await _controller.Recent(null, 15));
        Assert.IsType<BadRequestObjectResult>(await _controller.Recent(DateTime.UtcNow, 0));
    }

    [Fact]
    public async Task Ask_WhenSessionNotFound_ReturnsNotFound()
    {
        _sessionRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((ChatSession?)null);

        var result = await _controller.Ask(new ChatAskRequest { ChatSessionId = 99, Question = "What are my rights?" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Ask_WhenUserDoesNotOwnSession_ReturnsForbid()
    {
        var session = new ChatSession
        {
            ChatSessionId = 15,
            UserId = 999, // Different user
            Title = "Private Chat"
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);

        var result = await _controller.Ask(new ChatAskRequest { ChatSessionId = 15, Question = "Can I access this?" });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Commit_WhenSessionNotFound_ReturnsNotFound()
    {
        _sessionRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((ChatSession?)null);

        var request = new ChatCommitRequest
        {
            ChatSessionId = 99,
            CategoryId = 1,
            DistrictId = 1,
            Title = "My Case"
        };
        var result = await _controller.Commit(request);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Commit_WhenUserDoesNotOwnSession_ReturnsForbid()
    {
        var session = new ChatSession
        {
            ChatSessionId = 15,
            UserId = 999, // Different user
            Title = "Private Chat"
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);

        var request = new ChatCommitRequest
        {
            ChatSessionId = 15,
            CategoryId = 1,
            DistrictId = 1,
            Title = "My Case"
        };
        var result = await _controller.Commit(request);

        Assert.IsType<ForbidResult>(result);
    }

    // AUD-1 (CA5391): the JSON-body POST actions must be CSRF-protected like
    // every other POST controller in the app. Reflection pins the attribute so
    // a refactor that silently drops it fails the suite.
    [Theory]
    [InlineData(nameof(ChatController.New))]
    [InlineData(nameof(ChatController.Ask))]
    [InlineData(nameof(ChatController.Commit))]
    public void PostActions_CarryValidateAntiForgeryToken(string actionName)
    {
        var method = typeof(ChatController).GetMethod(actionName)!;

        Assert.True(
            method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: false).Any(),
            $"ChatController.{actionName} is missing [ValidateAntiForgeryToken].");
    }

    // AUD-3: a denied reservation walls the request BEFORE the AI pipeline runs.
    [Fact]
    public async Task Ask_WhenQuotaReservationFails_ReturnsWallWithoutModelCall()
    {
        var session = new ChatSession
        {
            ChatSessionId = 15,
            UserId = 42, // same as the authenticated test user
            Title = "My Chat"
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);

        var store = new Mock<IAiTurnReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((TurnReservation?)null);
        var chatService = new ChatService(
            _sessionRepo.Object,
            Mock.Of<IRepository<ChatMessage>>(),
            Mock.Of<IRepository<Case>>(),
            Mock.Of<ICaseRepository>(),
            Mock.Of<IRepository<AnswerCache>>(),
            Mock.Of<IRightsExplanationService>(),
            null!,
            Mock.Of<IEncryptionService>(),
            Mock.Of<IScenarioMappingRepository>(),
            Mock.Of<IKeywordSectionSearch>(),
            Mock.Of<IRepository<District>>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IAiService>(),
            Mock.Of<IAiLogService>(),
            Mock.Of<IChatHistoryRepository>(),
            Mock.Of<IRepository<Notification>>());
        var controller = new ChatController(chatService, new AiBudgetService(store.Object), Mock.Of<IActSectionRepository>())
        {
            ControllerContext = _controller.ControllerContext
        };

        var result = await controller.Ask(new ChatAskRequest { ChatSessionId = 15, Question = "Why?" });

        var json = Assert.IsType<JsonResult>(result);
        var tier = (string)json.Value!.GetType().GetProperty("tier")!.GetValue(json.Value)!;
        Assert.Equal("wall", tier);
    }

    // A1: a FromCache turn made no model call, so the reserved turn must be
    // RELEASED (free turn) — same rule as heuristic-blocked turns.
    [Fact]
    public async Task Ask_WhenTurnServedFromCache_ReleasesReservationWithoutCharging()
    {
        var session = new ChatSession
        {
            ChatSessionId = 15,
            UserId = 42,
            Title = "My Chat",
            // The turn cache is keyed by the case file; without one it never hits.
            CaseFileJson = "{\"district\":\"Dhaka\",\"facts\":\"wage theft\"}"
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);

        var store = new Mock<IAiTurnReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TurnReservation(1, PaidWithCredit: false));

        var cacheRepo = new Mock<IRepository<AnswerCache>>();
        var description = ChatService.CaseFileToDescription("{\"district\":\"Dhaka\",\"facts\":\"wage theft\"}");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                ChatService.NormalizeQuestion("repeat?") + "|" + description + "|bn")));
        cacheRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<AnswerCache>
        {
            new()
            {
                QueryHash = hash,
                Question = description,
                Answer = "cached answer",
                CitedJson = "[{\"sectionId\":7,\"actTitle\":\"X\",\"sectionNumber\":\"1\",\"relevanceScore\":0.9}]",
                HitCount = 0,
                CreatedAt = DateTime.UtcNow
            }
        });

        var chatService = new ChatService(
            _sessionRepo.Object,
            Mock.Of<IRepository<ChatMessage>>(),
            Mock.Of<IRepository<Case>>(),
            Mock.Of<ICaseRepository>(),
            cacheRepo.Object,
            Mock.Of<IRightsExplanationService>(),
            null!,
            Mock.Of<IEncryptionService>(),
            Mock.Of<IScenarioMappingRepository>(),
            Mock.Of<IKeywordSectionSearch>(),
            Mock.Of<IRepository<District>>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IAiService>(),
            Mock.Of<IAiLogService>(),
            Mock.Of<IChatHistoryRepository>(),
            Mock.Of<IRepository<Notification>>());
        var controller = new ChatController(chatService, new AiBudgetService(store.Object), Mock.Of<IActSectionRepository>())
        {
            ControllerContext = _controller.ControllerContext
        };

        var result = await controller.Ask(new ChatAskRequest { ChatSessionId = 15, Question = "repeat?" });

        Assert.IsType<JsonResult>(result);
        store.Verify(s => s.ReleaseAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // A6: the citation endpoint returns the authoritative statutory text for
    // a section id (replayed messages only carry title/number).
    [Fact]
    public async Task Citation_ReturnsSectionTextForId()
    {
        var sectionRepo = new Mock<IActSectionRepository>();
        sectionRepo.Setup(r => r.GetBySectionIdsAsync(It.Is<IEnumerable<int>>(ids => ids.Contains(7))))
            .ReturnsAsync(new List<ActSection>
            {
                new()
                {
                    SectionId = 7,
                    SectionNumber = "5",
                    SectionText = "Statutory text here.",
                    Act = new Act { ActId = 1, Title = "Labour Act 2006" }
                }
            });
        var chatService = new ChatService(
            _sessionRepo.Object,
            Mock.Of<IRepository<ChatMessage>>(),
            Mock.Of<IRepository<Case>>(),
            Mock.Of<ICaseRepository>(),
            Mock.Of<IRepository<AnswerCache>>(),
            Mock.Of<IRightsExplanationService>(),
            null!,
            Mock.Of<IEncryptionService>(),
            Mock.Of<IScenarioMappingRepository>(),
            Mock.Of<IKeywordSectionSearch>(),
            Mock.Of<IRepository<District>>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IAiService>(),
            Mock.Of<IAiLogService>(),
            Mock.Of<IChatHistoryRepository>(),
            Mock.Of<IRepository<Notification>>());
        var controller = new ChatController(chatService, new AiBudgetService(DefaultReservationStore().Object), sectionRepo.Object)
        {
            ControllerContext = _controller.ControllerContext
        };

        var result = await controller.Citation(7);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal("Labour Act 2006", (string)json.Value!.GetType().GetProperty("actTitle")!.GetValue(json.Value)!);
        Assert.Equal("Statutory text here.", (string)json.Value.GetType().GetProperty("sectionText")!.GetValue(json.Value)!);
    }

    // A5: resume recomputes draft eligibility from the session's case file so
    // the draft card reappears after a reload.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Messages_ComputesCanDraftFromCaseFile(bool committed)
    {
        var session = new ChatSession
        {
            ChatSessionId = 15,
            UserId = 42,
            Title = "My Chat",
            CaseFileJson = "{\"district\":\"Dhaka\",\"category\":\"RtiRequest\",\"facts\":\"info needed\"}",
            Status = committed ? ChatSessionStatus.Committed : ChatSessionStatus.InProgress,
            CommittedCaseId = committed ? 90 : null
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);
        var messageRepo = new Mock<IRepository<ChatMessage>>();
        messageRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ChatMessage>
        {
            new() { ChatSessionId = 15, Role = "user", Content = "তথ্য চাই।" }
        });
        var chatService = new ChatService(
            _sessionRepo.Object,
            messageRepo.Object,
            Mock.Of<IRepository<Case>>(),
            Mock.Of<ICaseRepository>(),
            Mock.Of<IRepository<AnswerCache>>(),
            Mock.Of<IRightsExplanationService>(),
            null!,
            Mock.Of<IEncryptionService>(),
            Mock.Of<IScenarioMappingRepository>(),
            Mock.Of<IKeywordSectionSearch>(),
            Mock.Of<IRepository<District>>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IAiService>(),
            Mock.Of<IAiLogService>(),
            Mock.Of<IChatHistoryRepository>(),
            Mock.Of<IRepository<Notification>>());
        var controller = new ChatController(chatService, new AiBudgetService(DefaultReservationStore().Object), Mock.Of<IActSectionRepository>())
        {
            ControllerContext = _controller.ControllerContext
        };

        var result = await controller.Messages(15);

        var json = Assert.IsType<JsonResult>(result);
        var canDraft = (bool)json.Value!.GetType().GetProperty("canDraft")!.GetValue(json.Value)!;
        var categoryId = (int)json.Value.GetType().GetProperty("suggestedCategoryId")!.GetValue(json.Value)!;
        Assert.Equal(!committed, canDraft);
        Assert.Equal(committed, json.Value.GetType().GetProperty("committed")!.GetValue(json.Value));
        Assert.Equal(committed ? 90 : (int?)null, json.Value.GetType().GetProperty("caseId")!.GetValue(json.Value));
        Assert.Equal(3, categoryId);
    }

    // A2: search mode bypasses quota reservation (no model call happens).
    [Fact]
    public async Task Ask_WhenSearchMode_DoesNotReserveQuota()
    {
        var session = new ChatSession
        {
            ChatSessionId = 15,
            UserId = 42,
            Title = "My Chat"
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);

        var store = new Mock<IAiTurnReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TurnReservation(1, PaidWithCredit: false));

        var scenarioRepo = new Mock<IScenarioMappingRepository>();
        scenarioRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping>());
        var keywordSearch = new Mock<IKeywordSectionSearch>();
        keywordSearch.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<RetrievedSection>());
        var chatService = new ChatService(
            _sessionRepo.Object,
            Mock.Of<IRepository<ChatMessage>>(),
            Mock.Of<IRepository<Case>>(),
            Mock.Of<ICaseRepository>(),
            Mock.Of<IRepository<AnswerCache>>(),
            Mock.Of<IRightsExplanationService>(),
            null!,
            Mock.Of<IEncryptionService>(),
            scenarioRepo.Object,
            keywordSearch.Object,
            Mock.Of<IRepository<District>>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IAiService>(),
            Mock.Of<IAiLogService>(),
            Mock.Of<IChatHistoryRepository>(),
            Mock.Of<IRepository<Notification>>());
        var controller = new ChatController(chatService, new AiBudgetService(store.Object), Mock.Of<IActSectionRepository>())
        {
            ControllerContext = _controller.ControllerContext
        };

        await controller.Ask(new ChatAskRequest { ChatSessionId = 15, Question = "wages", Mode = "search" });

        store.Verify(s => s.TryReserveAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.ReleaseAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The CHAT_TURN reservation IS the charge (AI_LOG rows are no longer
    // counted), so a real model turn must KEEP it — releasing it would make
    // every real turn free and hand back a spent credit.
    [Fact]
    public async Task Ask_WhenRealTurnSucceeds_KeepsReservation()
    {
        var session = new ChatSession
        {
            ChatSessionId = 15,
            UserId = 42,
            Title = "My Chat"
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);

        var rights = new Mock<IRightsExplanationService>();
        rights.Setup(r => r.ExplainRightsAsync(It.IsAny<Case>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new RightsExplanationDto("Answer", new List<CitedSectionDto>(), "disc"));

        var store = new Mock<IAiTurnReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TurnReservation(1, PaidWithCredit: false));

        var chatService = new ChatService(
            _sessionRepo.Object,
            Mock.Of<IRepository<ChatMessage>>(),
            Mock.Of<IRepository<Case>>(),
            Mock.Of<ICaseRepository>(),
            Mock.Of<IRepository<AnswerCache>>(),
            rights.Object,
            null!,
            Mock.Of<IEncryptionService>(),
            Mock.Of<IScenarioMappingRepository>(),
            Mock.Of<MuktoAin.Domain.Interfaces.Services.IKeywordSectionSearch>(),
            Mock.Of<IRepository<District>>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IAiService>(),
            Mock.Of<IAiLogService>(),
            Mock.Of<IChatHistoryRepository>(),
            Mock.Of<IRepository<Notification>>());
        var controller = new ChatController(chatService, new AiBudgetService(store.Object), Mock.Of<IActSectionRepository>())
        {
            ControllerContext = _controller.ControllerContext
        };

        var result = await controller.Ask(new ChatAskRequest { ChatSessionId = 15, Question = "Unpaid wages?" });

        Assert.IsType<JsonResult>(result);
        store.Verify(s => s.ReleaseAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // A turn that throws gave the citizen no answer: its reservation (and a
    // spent credit) goes back.
    [Fact]
    public async Task Ask_WhenTurnThrows_ReleasesReservation()
    {
        var session = new ChatSession { ChatSessionId = 15, UserId = 42, Title = "My Chat",
            CaseFileJson = "{\"facts\":\"wage theft\"}" }; // reaches the (throwing) cache lookup
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);

        var store = new Mock<IAiTurnReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TurnReservation(5, PaidWithCredit: true));
        var cacheRepo = new Mock<IRepository<AnswerCache>>();
        cacheRepo.Setup(r => r.GetAllAsync()).ThrowsAsync(new InvalidOperationException("synthetic failure"));

        var chatService = new ChatService(
            _sessionRepo.Object,
            Mock.Of<IRepository<ChatMessage>>(),
            Mock.Of<IRepository<Case>>(),
            Mock.Of<ICaseRepository>(),
            cacheRepo.Object,
            Mock.Of<IRightsExplanationService>(),
            null!,
            Mock.Of<IEncryptionService>(),
            Mock.Of<IScenarioMappingRepository>(),
            Mock.Of<MuktoAin.Domain.Interfaces.Services.IKeywordSectionSearch>(),
            Mock.Of<IRepository<District>>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IAiService>(),
            Mock.Of<IAiLogService>(),
            Mock.Of<IChatHistoryRepository>(),
            Mock.Of<IRepository<Notification>>());
        var controller = new ChatController(chatService, new AiBudgetService(store.Object), Mock.Of<IActSectionRepository>())
        {
            ControllerContext = _controller.ControllerContext
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Ask(new ChatAskRequest { ChatSessionId = 15, Question = "Unpaid wages?" }));

        store.Verify(s => s.ReleaseAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ask_RejectsQuestionOverMaxLength_WithBilingualError()
    {
        var result = await _controller.Ask(new ChatAskRequest
        {
            ChatSessionId = 15,
            Question = new string('a', 2001),
            Language = "en"
        });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var json = Assert.IsAssignableFrom<object>(bad.Value);
        Assert.Contains("too long", json.ToString());
    }

    [Fact]
    public async Task Messages_ReturnsBlockedTrue_WhenSessionIsBlocked()
    {
        var session = new ChatSession
        {
            ChatSessionId = 99,
            UserId = 42,
            Status = ChatSessionStatus.Blocked,
            CaseFileJson = "{\"category\":\"LabourComplaint\",\"district\":\"Dhaka\"}"
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync(session);

        var result = await _controller.Messages(99);
        var json = Assert.IsType<JsonResult>(result);
        var val = json.Value!;
        var blockedProp = val.GetType().GetProperty("blocked");
        Assert.NotNull(blockedProp);
        Assert.True((bool)blockedProp.GetValue(val)!);

        var canDraftProp = val.GetType().GetProperty("canDraft");
        Assert.NotNull(canDraftProp);
        Assert.False((bool)canDraftProp.GetValue(val)!);
    }

    [Fact]
    public async Task Ask_WhenSessionIsBlocked_ReturnsSessionBlockedTrue()
    {
        var session = new ChatSession
        {
            ChatSessionId = 105,
            UserId = 42,
            Status = ChatSessionStatus.Blocked,
            BlockedStreak = 3
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(105)).ReturnsAsync(session);

        var result = await _controller.Ask(new ChatAskRequest { ChatSessionId = 105, Question = "hello" });
        var json = Assert.IsType<JsonResult>(result);
        var val = json.Value!;
        var sessionBlockedProp = val.GetType().GetProperty("sessionBlocked");
        Assert.NotNull(sessionBlockedProp);
        Assert.True((bool)sessionBlockedProp.GetValue(val)!);
    }
}

