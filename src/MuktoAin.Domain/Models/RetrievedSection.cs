using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Models;

public record RetrievedSection(
    int SectionId,
    string ActTitle,
    string SectionNumber,
    string SectionText,
    float RelevanceScore,
    RetrievalMethod Method,
    string ActNumber = "",
    int ActYear = 0);
