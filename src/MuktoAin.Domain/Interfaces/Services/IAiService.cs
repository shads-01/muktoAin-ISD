namespace MuktoAin.Domain.Interfaces.Services;

// Implemented in Infrastructure (Gemini client).
public interface IAiService
{
    Task<string> GenerateContentAsync(string prompt);
}
