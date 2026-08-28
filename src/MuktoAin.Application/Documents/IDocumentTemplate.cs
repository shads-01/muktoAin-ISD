using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Documents;

/// <summary>
/// Template interface for structured legal document generation.
/// Each implementation declares which <see cref="DocumentType"/> it handles
/// and renders a complete document from a case + AI explanation.
/// Placed in Application (not Domain) because it references Application-layer DTOs.
/// </summary>
public interface IDocumentTemplate
{
    DocumentType DocumentType { get; }
    Task<string> RenderAsync(Case caseEntity, RightsExplanationDto explanation);
}
