namespace MuktoAin.IntegrationTests.Support;

// Splits SSMS-style scripts (scripts/*.sql) into executable T-SQL batches:
// `GO` is a client directive that ADO.NET rejects, so batches are split on it;
// `USE MuktoAin;` lines are removed because the fixture's connection string
// already targets MuktoAin_IntegrationTest. SET-option batches (e.g. SET
// QUOTED_IDENTIFIER ON in 02_schema.sql) survive because all batches run on
// one open connection, and SET options persist per session.
public static class SqlScriptRunner
{
    public static IReadOnlyList<string> SplitIntoBatches(string script)
    {
        var batches = new List<string>();
        var current = new List<string>();

        foreach (var line in script.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("USE ", StringComparison.OrdinalIgnoreCase))
            {
                continue; // catalog comes from the connection string, not the script
            }
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                Flush(batches, current);
            }
            else
            {
                current.Add(line);
            }
        }
        Flush(batches, current);
        return batches;
    }

    private static void Flush(List<string> batches, List<string> current)
    {
        var batch = string.Join("\n", current).Trim();
        current.Clear();
        if (batch.Length > 0)
        {
            batches.Add(batch);
        }
    }
}
