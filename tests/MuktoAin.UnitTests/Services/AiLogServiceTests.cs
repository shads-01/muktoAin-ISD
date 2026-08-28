using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.UnitTests.Services;

public class AiLogServiceTests
{
    private readonly Mock<IRepository<AiLog>> _logRepoMock = new();
    private readonly AiLogService _service;

    public AiLogServiceTests()
    {
        _service = new AiLogService(_logRepoMock.Object);
    }

    [Fact]
    public async Task LogAsync_RedactsProblemDescriptionInPromptAndSavesLog()
    {
        AiLog? savedLog = null;
        _logRepoMock.Setup(r => r.AddAsync(It.IsAny<AiLog>()))
            .Callback<AiLog>(log => savedLog = log)
            .Returns(Task.CompletedTask);

        var rawPrompt = """
            You are a legal information assistant for Bangladesh.
            A citizen has described this problem: My employer has not paid my salary for 3 months and threatened to terminate me without notice.

            Based ONLY on the following statutory sections, explain their rights
            """;

        await _service.LogAsync(
            caseId: 42,
            type: AiRequestType.RightsExplanation,
            prompt: rawPrompt,
            response: "Under Section 123 of Bangladesh Labour Act...",
            model: "gemini-2.0-flash",
            tokensUsed: 350,
            latencyMs: 1200);

        Assert.NotNull(savedLog);
        Assert.Equal(42, savedLog.CaseId);
        Assert.Equal(AiRequestType.RightsExplanation, savedLog.RequestType);
        Assert.Equal("Under Section 123 of Bangladesh Labour Act...", savedLog.ResponseText);
        Assert.Equal(1200, savedLog.LatencyMs);
        Assert.Equal(350, savedLog.TokensUsed);

        Assert.DoesNotContain("My employer has not paid my salary", savedLog.PromptText);
        Assert.Contains("[REDACTED: problem description", savedLog.PromptText);
        _logRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WithNullCaseId_SavesLogSuccessfully()
    {
        AiLog? savedLog = null;
        _logRepoMock.Setup(r => r.AddAsync(It.IsAny<AiLog>()))
            .Callback<AiLog>(log => savedLog = log)
            .Returns(Task.CompletedTask);

        await _service.LogAsync(
            caseId: null,
            type: AiRequestType.LawIdentification,
            prompt: "Test prompt",
            response: "Test response",
            model: "gemini-2.0-flash",
            tokensUsed: 100,
            latencyMs: 500);

        Assert.NotNull(savedLog);
        Assert.Null(savedLog.CaseId);
        _logRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }
}
