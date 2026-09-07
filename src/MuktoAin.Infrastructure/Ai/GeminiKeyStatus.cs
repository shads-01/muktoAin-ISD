namespace MuktoAin.Infrastructure.Ai;

/// <summary>
/// One Gemini API key's usage snapshot for the admin dashboard tracker
/// (see GeminiClient.Snapshot()). Never carries the actual key value.
/// </summary>
public sealed record GeminiKeyStatus(
    string Label,
    int RequestsToday,
    int DailyLimit,
    bool IsParked,
    DateTime? ParkedUntilUtc);
