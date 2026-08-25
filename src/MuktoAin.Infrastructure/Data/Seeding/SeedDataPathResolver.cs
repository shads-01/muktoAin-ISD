namespace MuktoAin.Infrastructure.Data.Seeding;

// Seed JSON files live at the repo root's `data/` directory, not under any project
// folder, so `contentRootPath` (the Web project's own folder) doesn't contain them
// directly. Walk upward until a sibling `data/<fileName>` is found. Both
// src/MuktoAin.Web and tests/* sit exactly 2 levels below the repo root, so 4
// gives headroom for that without risking a match against an unrelated ancestor
// directory named "data".
internal static class SeedDataPathResolver
{
    private const int MaxLevelsUp = 4;

    public static string Resolve(string contentRootPath, string fileName)
    {
        var dir = new DirectoryInfo(contentRootPath);
        for (var i = 0; i < MaxLevelsUp && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data", fileName);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            $"Could not locate '{fileName}' in a 'data' directory above '{contentRootPath}'.", fileName);
    }
}
