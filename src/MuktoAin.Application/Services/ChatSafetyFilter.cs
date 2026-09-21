using System.Text.RegularExpressions;
using MuktoAin.Domain.Constants;

namespace MuktoAin.Application.Services;

// Free C# heuristic pre-filter (spec 3.4, layer 1): trips before any model
// call, so blocked turns cost nothing. The envelope "intent" field is the
// model-side backstop (layer 2), handled in ChatService. Not a moderation
// engine — only clear-cut abuse; everything else flows through the intake
// conversation.
public class ChatSafetyFilter
{
    // ponytail: substring matching is coarse (a Bangla legal text quoting
    // "মিথ্যা মামলা" about someone ELSE could false-positive); upgrade to a
    // small classifier if trip reports show real citizens getting blocked.
    public bool IsBlocked(string message, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(message)) return false;
        var m = message.Trim();

        if (MatchesAny(m, ChatSafetyLists.CrimeFacilitation)) { reason = "crime_facilitation"; return true; }
        if (MatchesAny(m, ChatSafetyLists.FalseAccusation) || MatchesAny(m, ChatSafetyLists.Threats))
        { reason = "false_accusation"; return true; }
        if (MatchesAny(m, ChatSafetyLists.Injection)) { reason = "prompt_injection"; return true; }
        if (IsGibberish(m)) { reason = "gibberish"; return true; }
        return false;
    }

    private static bool MatchesAny(string message, string[] phrases)
        => phrases.Any(p => message.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsGibberish(string message)
    {
        if (Regex.IsMatch(message, @"(.)\1{9,}")) return true; // aaaaaaaaaa...
        var letters = message.Count(char.IsLetterOrDigit);
        return letters < 3; // "!!!", "///", single emoji spam
    }
}
