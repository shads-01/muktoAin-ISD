namespace MuktoAin.Domain.Interfaces.Services;

// Implemented in Infrastructure (Gemini text-embedding-004 client).
public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text);
}
