namespace MuktoAin.Domain.Constants;

/// <summary>
/// Bilingual section headings for LabourComplaintTemplate. Headers are static,
/// template-owned strings (not AI-authored) — kept here so both languages are
/// known without needing a translation call for them.
/// </summary>
public static class LabourComplaintHeadings
{
    public static readonly (string Bn, string En) FactsOfTheCase = ("মামলার ঘটনা", "Facts of the Case");
    public static readonly (string Bn, string En) ApplicableLegalProvisions = ("প্রযোজ্য আইনি বিধান", "Applicable Legal Provisions");
    public static readonly (string Bn, string En) YourRights = ("আপনার অধিকার", "Your Rights");
    public static readonly (string Bn, string En) ReliefSought = ("প্রার্থিত প্রতিকার", "Relief Sought");
    public static readonly (string Bn, string En) Declaration = ("ঘোষণা", "Declaration");

    public static IReadOnlyList<(string Bn, string En)> All { get; } = new[]
    {
        FactsOfTheCase, ApplicableLegalProvisions, YourRights, ReliefSought, Declaration
    };

    /// <summary>Picks the heading text matching the case's language ("en" = English, anything else = Bangla).</summary>
    public static string For((string Bn, string En) heading, string language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? heading.En : heading.Bn;
}
