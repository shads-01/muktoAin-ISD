using System.Text.RegularExpressions;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Application.Services;

public class AiLogService : IAiLogService
{
    private readonly IRepository<AiLog> _logRepo;
    private static readonly Regex ProblemRegex = new(
        @"(A citizen has described this problem:\s*)([\s\S]*?)(\r?\n\r?\n|\r?\nThe document type requested is:|\r?\nBased ONLY on the following)",
        RegexOptions.Compiled);

    public AiLogService(IRepository<AiLog> logRepo)
    {
        _logRepo = logRepo;
    }

    public async Task LogAsync(
        int? caseId,
        AiRequestType type,
        string prompt,
        string response,
        string model,
        int tokensUsed,
        int latencyMs,
        CancellationToken ct = default)
    {
        var redactedPrompt = RedactPii(prompt);

        var log = new AiLog
        {
            CaseId = caseId,
            RequestType = type,
            PromptText = redactedPrompt,
            ResponseText = response ?? string.Empty,
            ModelUsed = model ?? string.Empty,
            TokensUsed = tokensUsed,
            LatencyMs = latencyMs,
            CreatedAt = DateTime.UtcNow
        };

        await _logRepo.AddAsync(log);
        await _logRepo.SaveChangesAsync();
    }

    public static string RedactPii(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        return ProblemRegex.Replace(prompt, match =>
        {
            var prefix = match.Groups[1].Value;
            var problemText = match.Groups[2].Value;
            var suffix = match.Groups[3].Value;

            var redacted = $"[REDACTED: problem description, {problemText.Trim().Length} chars]";
            return $"{prefix}{redacted}{suffix}";
        });
    }
}
