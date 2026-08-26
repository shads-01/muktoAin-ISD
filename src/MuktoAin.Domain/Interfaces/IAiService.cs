namespace MuktoAin.Domain.Interfaces;

/// <summary>
/// Abstraction over the AI generation provider (Gemini).
/// Implemented by Infrastructure (e.g., GeminiClient).
/// </summary>
public interface IAiService
{
    Task<string> GenerateContentAsync(string prompt, CancellationToken ct = default);

    Task<float[]> EmbedContentAsync(string text, CancellationToken ct = default);
}
