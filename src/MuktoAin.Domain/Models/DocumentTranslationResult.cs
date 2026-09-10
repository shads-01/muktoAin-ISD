namespace MuktoAin.Domain.Models;

/// <summary>Result of a document translation request (cache hit or fresh AI translation).</summary>
public record DocumentTranslationResult(string Content, bool IsTranslated, string Disclaimer);
