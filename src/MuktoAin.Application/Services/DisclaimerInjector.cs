using MuktoAin.Domain.Constants;

namespace MuktoAin.Application.Services;

public class DisclaimerInjector
{
    /// <summary>
    /// Appends the appropriate legal disclaimer (surface 2 of 3) to an AI response.
    /// Never mutates the input; always returns a new string.
    /// Ensures idempotency so duplicate disclaimers are not stacked.
    /// </summary>
    public string InjectDisclaimer(string aiResponse, string language)
    {
        var disclaimer = Disclaimers.ForLanguage(language);
        var trimmed = aiResponse?.TrimEnd() ?? string.Empty;

        if (trimmed.EndsWith(disclaimer.Trim(), StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(Disclaimers.Legal.Trim(), StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(Disclaimers.LegalBangla.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            return disclaimer;
        }

        return $"{trimmed}{Environment.NewLine}{Environment.NewLine}{disclaimer}";
    }
}
