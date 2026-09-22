namespace MuktoAin.Domain.Entities;

// Normalized-query hash -> cached AI answer (quota ladder tier 0).
// Repeat questions are served without spending Gemini quota.
public class AnswerCache
{
    public int AnswerCacheId { get; set; }

    // SHA-256 hex of the normalized question
    public string QueryHash { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;

    public string? CitedJson { get; set; }

    public int HitCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
