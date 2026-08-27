using System.Text.RegularExpressions;

namespace MuktoAin.Infrastructure.Common;

// Shared by KeywordSearchService (T-2.2) and SimilaritySearchService (T-2.1): both need
// to recover a display-ready section number from ActSection.SectionNumber, which
// ActImportService leaves null by design (the source data didn't reliably expose a
// clean per-section number field, so ingestion relies on OrdinalPosition for ordering
// instead). The number is usually still there, embedded as the leading token of
// SectionText itself (e.g. "7. Every expression which is explained..."), so for
// presentation purposes it's recovered from there when the column is empty. This is
// presentation-layer inference, not a fix to the underlying data -- SectionNumber stays
// null in the database, and a section whose text doesn't start with a bare number
// (uncommon in this corpus, but not universal) still falls back to blank.
internal static class SectionNumberResolver
{
    private static readonly Regex LeadingSectionNumberPattern =
        new(@"^\s*(\d{1,4})\s*[.।]\s+", RegexOptions.Compiled);

    public static string Resolve(string? storedNumber, string sectionText)
    {
        if (!string.IsNullOrWhiteSpace(storedNumber))
        {
            return storedNumber;
        }

        var match = LeadingSectionNumberPattern.Match(sectionText);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
