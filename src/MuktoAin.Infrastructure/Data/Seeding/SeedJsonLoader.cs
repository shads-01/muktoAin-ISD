using System.Text.Json;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Shared load/deserialize step for every SeedXxx loader: resolve the seed file's
// path under data/, read it, and deserialize it. Keeps the serializer options and
// the "empty file" guard in one place instead of duplicated per seeder.
internal static class SeedJsonLoader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static Task<List<T>> LoadAsync<T>(string contentRootPath, string fileName)
        => LoadObjectAsync<List<T>>(contentRootPath, fileName);

    // For seed files whose root is a single object (e.g. { "acts": [...] }) rather
    // than a bare array.
    public static async Task<T> LoadObjectAsync<T>(string contentRootPath, string fileName)
    {
        var path = SeedDataPathResolver.Resolve(contentRootPath, fileName);
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidOperationException($"'{path}' deserialized to no data.");
    }
}
