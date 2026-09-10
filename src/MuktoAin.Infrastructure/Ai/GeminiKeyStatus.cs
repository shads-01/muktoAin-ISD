namespace MuktoAin.Infrastructure.Ai;

/// <summary>
/// One Gemini API key's usage snapshot for the admin dashboard tracker
/// (see GeminiClient.Snapshot()). Never carries the actual key value.
/// TokensUsedLastMinute covers GENERATION calls only — see
/// GeminiOptions.GenerationTokenLimitPerMinute for why embeddings aren't counted.
/// </summary>
public sealed record GeminiKeyStatus(
    string Label,
    int TokensUsedLastMinute,
    int TokenLimitPerMinute,
    bool IsParked,
    DateTime? ParkedUntilUtc);
