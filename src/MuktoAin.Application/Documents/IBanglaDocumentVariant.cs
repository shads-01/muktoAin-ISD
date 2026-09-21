using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Documents;

/// <summary>
/// Bangla-only rendering variant (A-3.9). Implemented by each IDocumentTemplate:
/// emits the full legal-document skeleton with NO English template copy —
/// all fixed text (headers, section labels, boilerplate) in Bangla only.
/// Citizen-supplied facts and AI explanation text are data, not template copy,
/// and pass through verbatim. The disclaimer stamp uses Disclaimers.LegalBangla
/// only, preserving the mandatory surface-3 disclaimer without English.
/// </summary>
public interface IBanglaDocumentVariant
{
    DocumentType DocumentType { get; }
    Task<string> RenderBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation);
}
