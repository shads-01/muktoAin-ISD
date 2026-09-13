using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Web.Controllers;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class ChatControllerTests
{
    private readonly Mock<IRepository<ChatSession>> _sessionRepo = new();
    private readonly ChatController _controller;

    private static Mock<IAiTurnReservationStore> DefaultReservationStore()
    {
        var store = new Mock<IAiTurnReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);
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
            Mock.Of<MuktoAin.Domain.Interfaces.Services.IKeywordSectionSearch>());

        var budgetService = new AiBudgetService(
            Mock.Of<IRepository<AiLog>>(), DefaultReservationStore().Object);

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

        _controller = new ChatController(chatService, budgetService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
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
        store.Setup(s => s.TryReserveAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);
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
            Mock.Of<IKeywordSectionSearch>());
        var controller = new ChatController(chatService, new AiBudgetService(Mock.Of<IRepository<AiLog>>(), store.Object))
        {
            ControllerContext = _controller.ControllerContext
        };

        var result = await controller.Ask(new ChatAskRequest { ChatSessionId = 15, Question = "Why?" });

        var json = Assert.IsType<JsonResult>(result);
        var tier = (string)json.Value!.GetType().GetProperty("tier")!.GetValue(json.Value)!;
        Assert.Equal("wall", tier);
    }

    // AUD-3 (double-count guard): the orchestration pipeline logs its own
    // AI_LOG row for a real model turn, so the controller must RELEASE the
    // reservation row on the success path too — otherwise every real turn
    // counts twice against the daily quota (guests 10 -> 5 effective).
    [Fact]
    public async Task Ask_WhenRealTurnSucceeds_ReleasesReservation()
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
        store.Setup(s => s.TryReserveAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

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
            Mock.Of<IKeywordSectionSearch>());
        var controller = new ChatController(chatService, new AiBudgetService(Mock.Of<IRepository<AiLog>>(), store.Object))
        {
            ControllerContext = _controller.ControllerContext
        };

        var result = await controller.Ask(new ChatAskRequest { ChatSessionId = 15, Question = "Unpaid wages?" });

        Assert.IsType<JsonResult>(result);
        store.Verify(s => s.ReleaseOneAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
