namespace MuktoAin.Infrastructure.Ai;

public class TokenRouterOptions
{
    public const string SectionName = "TokenRouter";

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.tokenrouter.com/v1";

    public string GenerationModel { get; set; } = "z-ai/glm-5.3-free";

    public int RetryCount { get; set; } = 3;

    public double RetryBaseDelaySeconds { get; set; } = 1;

    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    public double CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    public double RequestTimeoutSeconds { get; set; } = 60;
}
