namespace MuktoAin.Application.Services;

// Resolves files under the repo root's `data/` directory, mirroring
// Infrastructure's SeedDataPathResolver but starting from AppContext.BaseDirectory,
// which is much deeper (tests/*/bin/Debug/net8.0) — hence MaxLevelsUp 8, not 4.
internal static class BenchmarkDataPathResolver
{
    private const int MaxLevelsUp = 8;

    public static bool TryResolve(string startDirectory, string relativePathUnderData, out string path)
    {
        var dir = new DirectoryInfo(startDirectory);
        for (var i = 0; i < MaxLevelsUp && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data", relativePathUnderData);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    // For benchmark result outputs: anchors on the first existing `data/` directory
    // above the caller and creates the target subdirectory if needed.
    public static string ResolveResultsOutputPath(string startDirectory, string relativePathUnderData)
    {
        var dir = new DirectoryInfo(startDirectory);
        for (var i = 0; i < MaxLevelsUp && dir is not null; i++, dir = dir.Parent)
        {
            var dataDir = Path.Combine(dir.FullName, "data");
            if (!Directory.Exists(dataDir)) continue;

            var fullPath = Path.Combine(dataDir, relativePathUnderData);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return fullPath;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate a 'data' directory at or above '{startDirectory}'.");
    }
}
