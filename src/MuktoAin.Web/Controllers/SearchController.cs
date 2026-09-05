using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

public class SearchController : Controller
{
    private const int PageSize = 10;
    private const int SnippetLength = 220;

    // Matches a legal sub-clause marker like "(a)", "(1)", "(iv)", or the Bengali
    // equivalents this corpus's Bengali-language Acts actually use -- "(ক)" (a
    // Bengali consonant, structurally the same role as "a") and "(১)" (a Bengali
    // digit) -- followed by whitespace (a literal tab is common in the ingested
    // text, e.g. "comprise-(a)\tgrants from the Government;(b) \tloans...").
    private static readonly Regex ClauseMarkerPattern =
        new(@"\((?<marker>[a-zA-Z]{1,3}|[0-9]{1,3}|[ক-হ]|[০-৯]{1,3})\)\s+", RegexOptions.Compiled);

    // Markers a real enumerated list plausibly starts with. A single stray "(2)"
    // mid-sentence (e.g. a cross-reference to "section 3(2)") shouldn't be treated
    // as the start of a list -- requiring the first hit to look like a list-opener,
    // plus at least 2 hits total, keeps ordinary prose from getting bulleted.
    private static readonly HashSet<string> ListStartMarkers = new(StringComparer.Ordinal)
        { "a", "A", "1", "i", "I", "ক", "১" };

    private readonly SearchService _searchService;
    private readonly IActRepository _actRepo;

    public SearchController(SearchService searchService, IActRepository actRepo)
    {
        _searchService = searchService;
        _actRepo = actRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, int page = 1, int? actId = null)
    {
        // Act filter dropdown data (always, so the filter renders on empty state too)
        var acts = await _actRepo.GetAllAsync();
        ViewBag.Acts = acts.OrderBy(a => a.Title).Select(a => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
        {
            Value = a.ActId.ToString(),
            Text = $"{a.Title} ({a.Year})"
        }).ToList();

        if (string.IsNullOrWhiteSpace(q))
        {
            return View(new SearchViewModel());
        }

        var result = await _searchService.SearchActsAsync(q, page, PageSize, actId);
        var vm = ToViewModel(result);
        vm.ActId = actId;
        return View(vm);
    }

    private static SearchViewModel ToViewModel(SearchResultDto result)
    {
        return new SearchViewModel
        {
            Query = result.Query,
            Page = result.Page,
            PageSize = PageSize,
            TotalResults = result.TotalResults,
            Results = result.Results.Select(ToItemViewModel).ToList(),
        };
    }

    private static SearchResultItemViewModel ToItemViewModel(CitedSectionDto section)
    {
        // CitedSectionDto has no SectionTitle -- ACT_SECTION's title column exists but
        // RetrievedSection (the model this DTO is built from) doesn't carry it through
        // yet, so this is left blank rather than guessed.
        var (intro, clauses) = SplitIntoClauses(section.SectionText);

        return new SearchResultItemViewModel
        {
            SectionId = section.SectionId,
            ActTitle = section.ActTitle,
            SectionNumber = section.SectionNumber,
            SectionTitle = string.Empty,
            SectionTextSnippet = Truncate(section.SectionText, SnippetLength),
            SectionTextFull = section.SectionText,
            ActNumber = section.ActNumber,
            ActYear = section.ActYear,
            IsTruncated = section.SectionText.Length > SnippetLength,
            SectionIntro = intro,
            SectionClauses = clauses,
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength].TrimEnd() + "…";
    }

    // Splits statutory text into a lead-in sentence plus its enumerated sub-clauses,
    // e.g. "9. The Commission shall have its own Fund which shall comprise-" +
    // ["(a) grants from the Government;", "(b) loans from the Government;", ...] --
    // for display as a bulleted list in the full-section modal instead of one dense
    // paragraph. Falls back to (text, empty list) for ordinary prose with no real
    // enumeration, so plain sections still render as a single paragraph.
    private static (string Intro, List<string> Clauses) SplitIntoClauses(string text)
    {
        var matches = ClauseMarkerPattern.Matches(text);
        if (matches.Count < 2 || !ListStartMarkers.Contains(matches[0].Groups["marker"].Value))
        {
            return (text, new List<string>());
        }

        var intro = text[..matches[0].Index].Trim();
        var clauses = new List<string>();
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var clause = Regex.Replace(text[start..end], @"\s+", " ").Trim();
            if (clause.Length > 0)
            {
                clauses.Add(clause);
            }
        }

        return (intro, clauses);
    }
}
