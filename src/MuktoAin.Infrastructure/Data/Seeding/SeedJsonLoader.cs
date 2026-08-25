using System.Text.Json;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Shared load/deserialize step for every SeedXxx loader: resolve the seed file's
// path under data/, read it, and deserialize it. Keeps the serializer options and
// the "empty file" guard in one place instead of duplicated per seeder.
internal static class SeedJsonLoader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static async Task<List<T>> LoadAsync<T>(string contentRootPath, string fileName)
    {
        var path = SeedDataPathResolver.Resolve(contentRootPath, fileName);
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<T>>(json, Options)
            ?? throw new InvalidOperationException($"'{path}' deserialized to no rows.");
    }
}
