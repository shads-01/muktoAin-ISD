using Microsoft.AspNetCore.Http;

namespace MuktoAin.Web.Session;

public static class TrackedCases
{
    private const string Key = "TrackedCases";

    public static List<(int caseId, string code)> Read(ISession session)
    {
        var raw = session.GetString(Key);
        if (string.IsNullOrEmpty(raw)) return new List<(int, string)>();
        return raw.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Split(':', 2))
            .Where(p => p.Length == 2 && int.TryParse(p[0], out _))
            .Select(p => (int.Parse(p[0]), p[1])).ToList();
    }

    public static string? Resolve(ISession session, int caseId)
        => Read(session).FirstOrDefault(t => t.caseId == caseId).code;

    public static void Remember(ISession session, int caseId, string? code)
    {
        if (string.IsNullOrEmpty(code)) return;
        var entries = Read(session);
        if (entries.Any(t => t.caseId == caseId)) return;
        entries.Add((caseId, code));
        session.SetString(Key, string.Join("|", entries.Select(t => $"{t.caseId}:{t.code}")));
    }
}
