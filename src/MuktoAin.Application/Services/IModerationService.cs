namespace MuktoAin.Application.Services;

/// <summary>
/// Content moderation interface for validating user case submissions and text queries.
/// </summary>
public interface IModerationService
{
    /// <summary>
    /// Checks whether the provided text content is appropriate and free of blocked terms.
    /// </summary>
    /// <param name="content">The text content to inspect.</param>
    /// <returns>True if the content is appropriate; false if it contains prohibited/inappropriate terms.</returns>
    bool IsContentAppropriate(string? content);
}
