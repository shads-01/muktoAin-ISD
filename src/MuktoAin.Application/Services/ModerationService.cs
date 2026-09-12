namespace MuktoAin.Application.Services;

/// <summary>
/// Step 3.5: Moderation service for user case submissions and text content.
/// Uses a blocklist keyword filter to detect and reject inappropriate or prohibited content.
/// </summary>
public class ModerationService : IModerationService
{
    // Keyword filter blocklist (Bangla, English & transliterated terms)
    // Covers abusive, fraudulent, violent extremist, and illicit content.
    private static readonly HashSet<string> BlockedTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        // English terms
        "bomb making",
        "assassination",
        "hire killer",
        "hitman",
        "credit card fraud",
        "phishing attack",
        "exploit kit",
        "ransomware payout",
        "ddos attack",
        "counterfeit money",
        "child exploitation",
        "human trafficking ring",
        "illegal narcotics trade",
        "drug cartel",
        "buy weapons online",

        // Bangla terms (অপরাধমূলক ও উসকানিমূলক নিষিদ্ধ শব্দাবলী)
        "বোমা তৈরি",
        "হত্যাকাণ্ড ঘটানো",
        "জাল নোট তৈরি",
        "মাদক চোরাচালান",
        "হাতিয়ার বিক্রি",
        "অস্ত্র পাচার",
        "জঙ্গিবাদ",
        "সন্ত্রাসী হামলা",
        "সাইবার আক্রমণ",
        "অর্থ পাচার সিন্ডিকেট"
    };

    /// <summary>
    /// Checks whether the submission text is appropriate for legal processing.
    /// </summary>
    /// <param name="content">The text content submitted by the user.</param>
    /// <returns>True if appropriate; false if prohibited content is detected.</returns>
    public bool IsContentAppropriate(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        return !BlockedTerms.Any(term => content.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
