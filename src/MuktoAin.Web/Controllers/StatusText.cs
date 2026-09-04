using MuktoAin.Domain.Enums;

namespace MuktoAin.Web.Controllers;

/// <summary>
/// Bilingual display text for CaseStatus/DocumentStatus enum values and the
/// status-name strings the ViewModels carry (edited statuses like
/// "EditedApproved" never exist as enum members, so the string map is the
/// authoritative lookup; the enum arms are convenience for typed callers).
/// The badge CSS keys off the raw status name (badge-underreview etc.) — only
/// the VISIBLE text translates. Presentation mapping, not business logic.
/// </summary>
public static class StatusText
{
    private static readonly Dictionary<string, (string Bn, string En)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Submitted"] = ("দাখিলকৃত", "Submitted"),
        ["UnderReview"] = ("পর্যালোচনাধীন", "Under Review"),
        ["Finalized"] = ("চূড়ান্ত অনুমোদিত", "Approved & Final"),
        ["Draft"] = ("খসড়া", "Draft"),
        ["Approved"] = ("আইনজীবী অনুমোদিত", "Lawyer Approved"),
        ["EditedApproved"] = ("সম্পাদিত ও অনুমোদিত", "Edited & Approved"),
        ["Rejected"] = ("প্রত্যাখ্যাত", "Rejected"),
    };

    public static string Bn(object? status)
    {
        var key = status?.ToString();
        if (key is not null && Map.TryGetValue(key, out var v))
        {
            return v.Bn;
        }
        return key ?? string.Empty;
    }

    public static string En(object? status)
    {
        var key = status?.ToString();
        if (key is not null && Map.TryGetValue(key, out var v))
        {
            return v.En;
        }
        return key ?? string.Empty;
    }
}
