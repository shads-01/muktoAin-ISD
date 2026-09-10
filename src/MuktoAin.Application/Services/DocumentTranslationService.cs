using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.Application.Services;

/// <summary>
/// On-demand, cached translation of a generated document's full text.
/// Checks DocumentTranslation for an existing (DocumentId, Language) row
/// before calling Gemini; persists successful translations so repeat
/// toggles never re-call the AI. A too-short/empty AI result is treated
/// as a failure and is not cached (see spec's error handling section).
/// </summary>
public class DocumentTranslationService : IDocumentTranslationService
{
    private readonly IRepository<DocumentTranslation> _translationRepo;
    private readonly MuktoAin.Domain.Interfaces.IAiService _aiService;

    public DocumentTranslationService(
        IRepository<DocumentTranslation> translationRepo,
        MuktoAin.Domain.Interfaces.IAiService aiService)
    {
        _translationRepo = translationRepo;
        _aiService = aiService;
    }

    public async Task<DocumentTranslationResult> GetOrTranslateAsync(
        int documentId,
        string content,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default)
    {
        var disclaimer = Disclaimers.TranslationDisclaimer(sourceLanguage);

        var existing = await _translationRepo.GetAllAsync();
        var cached = existing.FirstOrDefault(t =>
            t.DocumentId == documentId &&
            string.Equals(t.Language, targetLanguage, StringComparison.OrdinalIgnoreCase));

        if (cached != null)
            return new DocumentTranslationResult(cached.TranslatedContent, true, disclaimer);

        var prompt = PromptTemplates.Translation
            .Replace("{sourceLanguage}", sourceLanguage)
            .Replace("{targetLanguage}", targetLanguage)
            .Replace("{content}", content);

        var translated = await _aiService.GenerateContentAsync(prompt, ct);

        // Cheap quality heuristic: empty, or drastically shorter than the
        // source, is treated as a failed translation rather than cached.
        if (string.IsNullOrWhiteSpace(translated) || translated.Length < content.Length / 3)
            throw new InvalidOperationException("Translation result failed the quality check.");

        var record = new DocumentTranslation
        {
            DocumentId = documentId,
            Language = targetLanguage,
            TranslatedContent = translated,
            CreatedAt = DateTime.UtcNow
        };
        await _translationRepo.AddAsync(record);
        await _translationRepo.SaveChangesAsync();

        return new DocumentTranslationResult(translated, true, disclaimer);
    }
}
