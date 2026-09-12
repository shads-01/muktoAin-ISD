using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Web.Controllers;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class ChatControllerTests
{
    private readonly Mock<IRepository<ChatSession>> _sessionRepo = new();
    private readonly ChatController _controller;

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
            Mock.Of<MuktoAin.Domain.Interfaces.IKeywordSectionSearch>());

        var budgetService = new AiBudgetService(Mock.Of<IRepository<AiLog>>());

        var httpContext = new DefaultHttpContext
        {
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
}
