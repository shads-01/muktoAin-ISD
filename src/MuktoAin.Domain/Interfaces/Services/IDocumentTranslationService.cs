using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Services;

/// <summary>
/// On-demand, cached translation of a generated document's full text.
/// Implemented in Application via the existing Gemini IAiService path.
/// </summary>
public interface IDocumentTranslationService
{
    Task<DocumentTranslationResult> GetOrTranslateAsync(
        int documentId,
        string content,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default);
}
