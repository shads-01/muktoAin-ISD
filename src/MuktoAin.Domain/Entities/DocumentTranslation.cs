namespace MuktoAin.Domain.Entities;

/// <summary>
/// Cache of on-demand AI translations of a GeneratedDocument's content into
/// a target language. One row per (DocumentId, Language). Never the
/// authoritative document — see DocumentTranslationResult.IsTranslated /
/// the fixed disclaimer surfaced alongside translated content.
/// </summary>
public class DocumentTranslation
{
    public int DocumentTranslationId { get; set; }

    public int DocumentId { get; set; }
    public GeneratedDocument Document { get; set; } = null!;

    // "bn" or "en" — the language TranslatedContent is written in.
    public string Language { get; set; } = string.Empty;

    public string TranslatedContent { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
