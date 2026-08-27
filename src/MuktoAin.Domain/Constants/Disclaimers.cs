namespace MuktoAin.Domain.Constants;

public static class Disclaimers
{
    public const string Legal =
        "⚠️ MuktoAin provides general legal information and document drafting assistance. " +
        "This is NOT formal legal advice. Every document must be reviewed by a verified lawyer " +
        "before use. For urgent legal matters, consult a qualified advocate.";

    public const string LegalBangla =
        "⚠️ মুক্ত আইন সাধারণ আইনি তথ্য ও নথি প্রণয়নে সহায়তা প্রদান করে। এটি আনুষ্ঠানিক আইনি পরামর্শ নয়। " +
        "প্রতিটি নথি ব্যবহারের পূর্বে একজন যাচাইকৃত আইনজীবী দ্বারা পর্যালোচনা করা আবশ্যক।";

    /// <summary>
    /// Returns the disclaimer for the requested language code ("bn" or "en").
    /// Defaults to the Bangla disclaimer for unknown codes.
    /// </summary>
    public static string ForLanguage(string language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? Legal : LegalBangla;
}
