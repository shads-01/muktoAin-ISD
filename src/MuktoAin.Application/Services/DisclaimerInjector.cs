using MuktoAin.Domain.Constants;

namespace MuktoAin.Application.Services;

public class DisclaimerInjector
{
    /// <summary>
    /// Appends the appropriate legal disclaimer (surface 2 of 3) to an AI response.
    /// Never mutates the input; always returns a new string.
    /// </summary>
    public string InjectDisclaimer(string aiResponse, string language)
    {
        var disclaimer = Disclaimers.ForLanguage(language);
        return $"{aiResponse?.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{disclaimer}";
    }
}
