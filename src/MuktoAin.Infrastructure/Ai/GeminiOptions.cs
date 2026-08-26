namespace MuktoAin.Infrastructure.Ai;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string[] ApiKeys { get; set; } = [];

    public string EmbeddingModel { get; set; } = "text-embedding-004";

    public string GenerationModel { get; set; } = "gemini-2.0-flash";

    // S-2.6 resilience knobs (see GeminiResiliencePolicies)
    public int RetryCount { get; set; } = 3;

    public double RetryBaseDelaySeconds { get; set; } = 1;

    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    public double CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    public double RequestTimeoutSeconds { get; set; } = 60;
}
