namespace MuktoAin.Domain.Interfaces.Services;

// Implemented in Infrastructure (Gemini embedding client).
public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text);
}
