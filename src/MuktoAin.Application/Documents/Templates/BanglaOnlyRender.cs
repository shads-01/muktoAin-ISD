using System.Globalization;

namespace MuktoAin.Application.Documents.Templates;

internal static class BanglaOnlyRender
{
    internal const string Rule = "────────────────────────────";
    internal const string DoubleRule = "════════════════════════════════════════════";
    internal const string Placeholder = "________";

    // Numeric date — Bangla-only documents must not carry English month names.
    internal static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Shared closing block: signature + Bangla-only disclaimer stamp
    /// (mandatory surface 3 of 3 — FR-11).
    /// </summary>
    internal static string AppendClosing(System.Text.StringBuilder sb, string roleLabelBn, string? districtName)
    {
        sb.AppendLine($"তারিখ: {Today()}");
        sb.AppendLine($"{roleLabelBn}: ________________________");
        sb.AppendLine($"জেলা: {districtName ?? Placeholder}");
        sb.AppendLine();
        sb.AppendLine(DoubleRule);
        sb.AppendLine(MuktoAin.Domain.Constants.Disclaimers.LegalBangla);
        sb.AppendLine(DoubleRule);
        return sb.ToString();
    }
}
