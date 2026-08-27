using System.Text.RegularExpressions;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.Infrastructure.Search;

// T-2.2: SQL Server Full-Text Search over ACT_SECTION (index built in T-1.10).
// Two callers:
//   - FR-7 standalone search: SearchService (Application) wraps this for citizens
//     browsing/searching the Acts corpus directly.
//   - FR-3 fallback: RagContextBuilder (T-2.3) calls this when the Qdrant vector
//     path (IVectorSectionSearch) is down or returns nothing.
public class KeywordSearchService : IKeywordSectionSearch
{
    private readonly IActSectionRepository _sectionRepo;

    public KeywordSearchService(IActSectionRepository sectionRepo)
    {
        _sectionRepo = sectionRepo;
    }

    public async Task<IEnumerable<RetrievedSection>> SearchAsync(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Enumerable.Empty<RetrievedSection>();
        }

        var ftsQuery = SanitizeForFts(query);
        if (string.IsNullOrEmpty(ftsQuery))
        {
            // Query was non-blank but reduced to nothing once quote characters were
            // stripped (e.g. a query that was only quote marks) -- CONTAINSTABLE
            // rejects an empty search condition, so short-circuit the same way a
            // blank query does rather than letting that hit the database as an error.
            return Enumerable.Empty<RetrievedSection>();
        }

        var sections = await _sectionRepo.FullTextSearchAsync(ftsQuery, maxResults);

        // FTS doesn't hand back a usable relevance score through FromSqlInterpolated's
        // entity projection (CONTAINSTABLE's RANK column is consumed inside the query
        // for ORDER BY only), so keyword results carry 0f here. RagContextBuilder treats
        // vector results as primary and only reaches this fallback when vector search
        // is unavailable, so cross-method score comparison isn't a live requirement yet.
        return sections.Select(s => new RetrievedSection(
            s.SectionId,
            s.Act.Title,
            DeriveSectionNumber(s.SectionNumber, s.SectionText),
            s.SectionText,
            0f,
            RetrievalMethod.Keyword,
            s.Act.ActNumber,
            s.Act.Year));
    }

    // ActSection.SectionNumber is left null by design (see ActImportService) --
    // the source data didn't reliably expose a clean per-section number field, so
    // ingestion relies on OrdinalPosition for ordering instead. But the number is
    // usually still there, embedded as the leading token of SectionText itself
    // (e.g. "7. Every expression which is explained..."), so for display purposes
    // it's recovered from there when the column is empty. This is presentation-layer
    // inference, not a fix to the underlying data -- SectionNumber stays null in the
    // database, and a section whose text doesn't start with a bare number (uncommon
    // in this corpus, but not universal) still falls back to blank.
    private static readonly Regex LeadingSectionNumberPattern =
        new(@"^\s*(\d{1,4})\s*[.।]\s+", RegexOptions.Compiled);

    private static string DeriveSectionNumber(string? storedNumber, string sectionText)
    {
        if (!string.IsNullOrWhiteSpace(storedNumber))
        {
            return storedNumber;
        }

        var match = LeadingSectionNumberPattern.Match(sectionText);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string SanitizeForFts(string query)
    {
        // CONTAINSTABLE's query string is itself a mini boolean-search grammar
        // ("word" OR "word", NEAR, FORMSOF, etc.) -- passing raw user input through
        // lets a bare double-quote or an operator character break the query or change
        // its meaning. Strip double quotes (the only character that can terminate a
        // wrapping phrase early), then build one AND-group per word, requiring every
        // word to appear somewhere in SectionText but not necessarily adjacent to each
        // other.
        //
        // Originally this wrapped the whole query as one exact phrase, which broke
        // multi-word queries where the terms exist in the corpus but not adjacent to
        // each other. AND-of-terms fixes that while still requiring every word to be
        // present (unlike OR, which would tank precision).
        //
        // No stemmer covers Bangla in SQL Server, so exact-word matching is also the
        // correct behavior for Bangla terms; English terms lose FORMSOF(INFLECTIONAL,
        // ...) stemming as a result -- acceptable for T-2.2's scope, revisit if English
        // recall turns out to matter.
        var words = query.Replace("\"", string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return string.Join(" AND ", words.Select(BuildWordGroup));
    }

    // Bengali digits (০-৯, U+09E6-U+09EF) and Latin digits (0-9) are different code
    // points to SQL Server's tokenizer -- "৪২০" and "420" never match each other even
    // though they mean the same number. This corpus is genuinely bilingual: older
    // English-language Acts number sections with Latin digits ("420. Whoever
    // cheats..."), while Bengali-language Acts number them with native Bengali digits
    // ("১৬।  অন্য কোনো..."). Converting a Bengali-digit query to Latin (or vice versa)
    // would only find one half of the corpus and silently drop the other -- so for a
    // word made entirely of digits, both forms are searched via an OR group instead of
    // committing to one script. A word with no digits is left as a single term.
    private static string BuildWordGroup(string word)
    {
        var converted = ConvertDigitScript(word);
        return converted == word
            ? $"\"{word}\""
            : $"(\"{word}\" OR \"{converted}\")";
    }

    // Converts a purely-Bengali-digit word to Latin digits, or a purely-Latin-digit
    // word to Bengali digits. Returns the input unchanged for anything mixed or
    // non-numeric, since there's no meaningful "other script" form of a word like
    // "labour" or "১৬।" (trailing punctuation breaks the "purely digits" check on
    // purpose -- only bare numbers get the alternate-script treatment).
    private static string ConvertDigitScript(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }

        var allBengali = true;
        var allLatin = true;
        foreach (var c in word)
        {
            if (c is < '০' or > '৯') allBengali = false;
            if (c is < '0' or > '9') allLatin = false;
        }

        if (allBengali)
        {
            return string.Create(word.Length, word, (span, source) =>
            {
                for (var i = 0; i < source.Length; i++) span[i] = (char)(source[i] - '০' + '0');
            });
        }

        if (allLatin)
        {
            return string.Create(word.Length, word, (span, source) =>
            {
                for (var i = 0; i < source.Length; i++) span[i] = (char)(source[i] - '0' + '০');
            });
        }

        return word;
    }
}
