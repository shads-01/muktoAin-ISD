using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Documents;

/// <summary>
/// Core document generation engine. Selects the appropriate template based on
/// the case's category and delegates rendering to the matched <see cref="IDocumentTemplate"/>.
/// Templates are auto-discovered via DI (IEnumerable&lt;IDocumentTemplate&gt;).
/// </summary>
public class DocumentGenerator
{
    private readonly Dictionary<DocumentType, IDocumentTemplate> _templates;

    public DocumentGenerator(IEnumerable<IDocumentTemplate> templates)
    {
        _templates = templates.ToDictionary(t => t.DocumentType);
    }

    /// <summary>
    /// Generates a structured legal document for the given case using the AI explanation context.
    /// </summary>
    public async Task<string> GenerateAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var docType = MapCategoryToDocumentType(caseEntity.CategoryId);

        if (!_templates.TryGetValue(docType, out var template))
            throw new InvalidOperationException($"No template found for document type {docType}");

        return await template.RenderAsync(caseEntity, explanation);
    }

    /// <summary>
    /// Bangla-only rendering path (A-3.9): fixed template copy is emitted in
    /// Bangla only (no English interleaving); citizen/AI content flows through
    /// verbatim. Routing decision (which language a case gets) is the caller's —
    /// DocumentService routes on Case.Language.
    /// </summary>
    public async Task<string> GenerateBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var docType = MapCategoryToDocumentType(caseEntity.CategoryId);

        if (!_templates.TryGetValue(docType, out var template))
            throw new InvalidOperationException($"No template found for document type {docType}");

        if (template is IBanglaDocumentVariant banglaVariant)
            return await banglaVariant.RenderBanglaOnlyAsync(caseEntity, explanation);

        return await template.RenderAsync(caseEntity, explanation);
    }

    /// <summary>
    /// Exposes the category→DocumentType mapping for callers that need the type
    /// without generating a full document (e.g., DocumentService persisting the enum).
    /// </summary>
    public DocumentType GetDocumentType(int categoryId) => MapCategoryToDocumentType(categoryId);

    // Mapping verified against data/categories.json:
    //   1 = Labour Complaint, 2 = General Diary (GD), 3 = RTI Request, 4 = Consumer Complaint
    private static DocumentType MapCategoryToDocumentType(int categoryId) => categoryId switch
    {
        1 => DocumentType.LabourComplaint,
        2 => DocumentType.GeneralDiary,
        3 => DocumentType.RtiRequest,
        4 => DocumentType.ConsumerComplaint,
        _ => throw new ArgumentException($"Unknown category: {categoryId}")
    };
}
