using MuktoAin.Application.Documents;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Application.Services;

/// <summary>
/// Manages the document lifecycle from AI-generated draft through lawyer review to finalization.
/// Key invariant: <c>ContentDraft</c> is NEVER modified after generation — it is the immutable
/// AI original. <c>ContentFinal</c> is set only after a lawyer approves (plain or edited).
/// </summary>
public class DocumentService
{
    private readonly DocumentGenerator _generator;
    private readonly IRepository<GeneratedDocument> _docRepo;
    private readonly ICaseRepository _caseRepo;
    private readonly IRepository<District> _districtRepo;
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IPdfExporter _pdfExporter;

    public DocumentService(
        DocumentGenerator generator,
        IRepository<GeneratedDocument> docRepo,
        ICaseRepository caseRepo,
        IRepository<District> districtRepo,
        IRepository<CaseCategory> categoryRepo,
        IPdfExporter pdfExporter)
    {
        _generator = generator;
        _docRepo = docRepo;
        _caseRepo = caseRepo;
        _districtRepo = districtRepo;
        _categoryRepo = categoryRepo;
        _pdfExporter = pdfExporter;
    }

    /// <summary>
    /// Generates a new draft document for a case using the AI explanation context.
    /// The case's District and Category are loaded for template rendering.
    /// </summary>
    public async Task<DraftDocumentDto> GenerateDocumentAsync(int caseId, RightsExplanationDto explanation)
    {
        var caseEntity = await _caseRepo.GetByIdAsync(caseId);
        if (caseEntity == null)
            throw new ArgumentException($"Case not found: {caseId}");

        // Ensure navigation properties are available for template rendering.
        // GetByIdAsync uses FindAsync which doesn't Include navigations,
        // so we load District and Category explicitly.
        if (caseEntity.District == null)
        {
            var district = await _districtRepo.GetByIdAsync(caseEntity.DistrictId);
            if (district != null) caseEntity.District = district;
        }
        if (caseEntity.Category == null)
        {
            var category = await _categoryRepo.GetByIdAsync(caseEntity.CategoryId);
            if (category != null) caseEntity.Category = category;
        }

        var content = await _generator.GenerateAsync(caseEntity, explanation);

        var doc = new GeneratedDocument
        {
            CaseId = caseId,
            DocumentType = _generator.GetDocumentType(caseEntity.CategoryId),
            ContentDraft = content,       // immutable AI original
            ContentFinal = null,           // filled after lawyer review
            Status = DocumentStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        await _docRepo.AddAsync(doc);
        await _docRepo.SaveChangesAsync();

        return MapToDto(doc);
    }

    /// <summary>
    /// Retrieves a document for preview. Citizens see ContentDraft; lawyers see both.
    /// Access control is the caller's responsibility (controller/service layer).
    /// </summary>
    public async Task<DraftDocumentDto?> GetDocumentAsync(int documentId)
    {
        var doc = await _docRepo.GetByIdAsync(documentId);
        return doc == null ? null : MapToDto(doc);
    }

    /// <summary>
    /// Updates document status (called by LawyerReviewService during A-2.7).
    /// For Approved without edits: ContentFinal = ContentDraft.
    /// For EditedApproved: ContentFinal = editedContent (ContentDraft preserved).
    /// For Rejected: status changes, no content modification.
    /// </summary>
    public async Task UpdateStatusAsync(int documentId, DocumentStatus newStatus, string? editedContent = null)
    {
        var doc = await _docRepo.GetByIdAsync(documentId);
        if (doc == null)
            throw new ArgumentException($"Document not found: {documentId}");

        doc.Status = newStatus;

        if (editedContent != null)
        {
            doc.ContentFinal = editedContent;
        }
        else if (newStatus == DocumentStatus.Approved)
        {
            // Approved without edits — final = draft
            doc.ContentFinal = doc.ContentDraft;
        }

        await _docRepo.SaveChangesAsync();
    }

    /// <summary>
    /// PDF download gate (FR-14 / review gate): returns PDF bytes ONLY for
    /// lawyer-approved documents; null otherwise. Citizens can never download
    /// a draft or rejected document. This is the non-negotiable human-in-the-loop
    /// safeguard — controllers must use this instead of calling IPdfExporter raw.
    /// </summary>
    public async Task<byte[]?> GetPdfIfApprovedAsync(int documentId)
    {
        var doc = await _docRepo.GetByIdAsync(documentId);
        if (doc?.Status != DocumentStatus.Approved) return null;

        // The generic repo does no Includes and there is no lazy loading —
        // resolve the parent Case explicitly (GeneratePdf needs jurisdiction data).
        var caseEntity = await _caseRepo.GetByIdAsync(doc.CaseId);
        if (caseEntity == null) return null;

        return _pdfExporter.GeneratePdf(doc, caseEntity);
    }

    private static DraftDocumentDto MapToDto(GeneratedDocument doc)
    {
        return new DraftDocumentDto(
            doc.DocumentId,
            doc.CaseId,
            doc.DocumentType.ToString(),
            doc.ContentDraft,
            doc.Status.ToString(),
            doc.CreatedAt);
    }
}
