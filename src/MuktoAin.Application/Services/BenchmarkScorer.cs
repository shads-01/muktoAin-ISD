using System.Text;
using System.Text.RegularExpressions;
using MuktoAin.Domain.Models;

namespace MuktoAin.Application.Services;

// A gold reference parsed out of a dataset row, e.g.
// "The Code of Civil Procedure, 1908, Section 2(2)".
public sealed record BenchmarkSectionReference(string ActTitle, string SectionNumber);

public static class BenchmarkScorer
{
    private static readonly Regex ReferenceRegex =
        new(@"^(?<act>.+?),\s*(?:Section|ধারা)\s*(?<sec>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Regex SectionBaseRegex =
        new(@"^(?<base>[0-9]+[A-Za-z]?)",
            RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Splits "Act Title, Section 2(2)" / "Act Title, ধারা ৩৫ক" into its two parts.
    /// Falls back to (whole string, "") when the ", Section"/", ধারা" separator is absent.
    /// </summary>
    public static BenchmarkSectionReference ParseReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new BenchmarkSectionReference(string.Empty, string.Empty);
        }

        var match = ReferenceRegex.Match(reference.Trim());
        return match.Success
            ? new BenchmarkSectionReference(match.Groups["act"].Value.Trim(), match.Groups["sec"].Value.Trim())
            : new BenchmarkSectionReference(reference.Trim(), string.Empty);
    }

    /// <summary>
    /// Folds Bangla digits to ASCII, lowercases, and keeps only letters/digits —
    /// used for act-title comparison so punctuation differences never break a match.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= '\u09E6' and <= '\u09EF')
            {
                sb.Append((char)('0' + (c - '\u09E6')));
            }
            else if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            // spaces, punctuation, and non-letter symbols are dropped
        }

        return sb.ToString();
    }

    /// <summary>
    /// Section-granularity key: "2(2)" → "2", "35A" → "35A", "৩৫ক" → "35".
    /// Citations in this repo operate at the section level
    /// (CASE_ACT_REFERENCE.SectionId), so clause suffixes are deliberately ignored.
    /// </summary>
    public static string BaseSectionNumber(string? sectionNumber)
    {
        if (string.IsNullOrWhiteSpace(sectionNumber)) return string.Empty;

        var digitMapped = MapBanglaDigits(sectionNumber.Trim());
        var stripped = Regex.Replace(digitMapped, @"^(?:section|sec\.?|ধারা)\s*", "", RegexOptions.IgnoreCase);
        var match = SectionBaseRegex.Match(stripped);
        return match.Success ? match.Groups["base"].Value : string.Empty;
    }

    public static bool IsMatch(BenchmarkSectionReference expected, RetrievedSection cited)
    {
        var expectedAct = Normalize(expected.ActTitle);
        var citedAct = Normalize(cited.ActTitle);

        // Containment either way handles "Bangladesh Labour Act 2006" vs
        // "Bangladesh Labour Act, 2006" style punctuation differences.
        var actMatches = citedAct.Length > 0 &&
            (expectedAct.Contains(citedAct, StringComparison.Ordinal) ||
             citedAct.Contains(expectedAct, StringComparison.Ordinal));
        if (!actMatches) return false;

        var expectedBase = BaseSectionNumber(expected.SectionNumber);
        var citedBase = BaseSectionNumber(cited.SectionNumber);
        return expectedBase.Length > 0 &&
               string.Equals(expectedBase, citedBase, StringComparison.Ordinal);
    }

    /// <summary>
    /// Citation-level precision/recall/F1 for one benchmark question:
    /// precision = hits / cited, recall = hits / expected.
    /// </summary>
    public static (double Precision, double Recall, double F1) ScoreQuestion(
        IReadOnlyList<string> expectedReferences,
        IReadOnlyList<RetrievedSection> citedSections)
    {
        var expected = (expectedReferences ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(ParseReference)
            .ToList();
        var cited = citedSections ?? Array.Empty<RetrievedSection>();

        if (expected.Count == 0 || cited.Count == 0) return (0, 0, 0);

        var hits = cited.Count(c => expected.Any(e => IsMatch(e, c)));
        var precision = (double)hits / cited.Count;
        var recall = (double)hits / expected.Count;
        var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
        return (precision, recall, f1);
    }

    private static string MapBanglaDigits(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c is >= '\u09E6' and <= '\u09EF' ? (char)('0' + (c - '\u09E6')) : c);
        }

        return sb.ToString();
    }
}
