using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Services;

// Implemented in Application (vector-primary retrieval with FTS fallback).
public interface IRagContextBuilder
{
    Task<IEnumerable<RetrievedSection>> RetrieveContextAsync(string query, int topK = 8);
}
