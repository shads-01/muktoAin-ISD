namespace MuktoAin.Infrastructure.Ai;

/// <summary>
/// Surfaces Gemini API failures with the status code and response body so callers
/// can present a clear user-facing message instead of a raw stack trace.
/// </summary>
public class GeminiApiException : Exception
{
    public int StatusCode { get; }

    public string? ResponseBody { get; }

    public GeminiApiException(string message, int statusCode, string? responseBody, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
