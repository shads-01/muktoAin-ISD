using System.Text;
using System.Text.RegularExpressions;

namespace MuktoAin.Application.Documents;

/// <summary>
/// Strips markdown syntax and collapses excess blank lines from AI-authored
/// text before it is embedded into a plain-text legal document. Applied only
/// to AI-authored spans (e.g. Gemini's rights explanation) — never to
/// citizen-authored free text or statute text.
/// </summary>
public static class AiTextSanitizer
{
    private static readonly Regex HeadingMarker = new(@"^\s{0,3}#{1,6}\s*", RegexOptions.Compiled);
    private static readonly Regex BulletMarker = new(@"^\s{0,3}[-*]\s+", RegexOptions.Compiled);

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var cleanedLines = new List<string>(lines.Length);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Replace("**", string.Empty).Replace("__", string.Empty);
            line = HeadingMarker.Replace(line, string.Empty);
            line = BulletMarker.Replace(line, string.Empty);
            cleanedLines.Add(line.TrimEnd());
        }

        var result = new List<string>(cleanedLines.Count);
        var blankStreak = 0;
        foreach (var line in cleanedLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankStreak++;
                if (blankStreak == 1)
                    result.Add(string.Empty);
            }
            else
            {
                blankStreak = 0;
                result.Add(line);
            }
        }

        return string.Join("\n", result).Trim();
    }
}
