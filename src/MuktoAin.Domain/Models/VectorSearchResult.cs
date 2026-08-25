namespace MuktoAin.Domain.Models;

public record VectorSearchResult(string VectorId, float Score, Dictionary<string, string> Payload);
