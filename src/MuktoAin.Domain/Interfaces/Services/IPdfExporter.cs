using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Services;

/// <summary>
/// Renders a lawyer-approved <see cref="GeneratedDocument"/> (with its parent
/// <see cref="Case"/>) into a PDF byte stream. Implemented by QuestPDF in
/// Infrastructure (A-2.5). Consumers must gate calls on
/// DocumentStatus.Approved — the human-in-the-loop review gate.
/// </summary>
public interface IPdfExporter
{
    /// <summary>
    /// Renders <c>ContentFinal ?? ContentDraft</c> to PDF with the Bangla font
    /// and the permanently-stamped bilingual disclaimer (Surface 3 of 3).
    /// </summary>
    byte[] GeneratePdf(GeneratedDocument document, Case caseEntity);
}
