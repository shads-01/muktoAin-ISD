using Microsoft.Extensions.Options;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace MuktoAin.Infrastructure.VectorStore;

// T-1.11: Qdrant.Client (gRPC) implementation of IVectorStore.
// ponytail: SDK handles connection pooling/retries internally -- not wrapped in Polly,
// that's for HTTP clients we control directly.
public class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;
    private readonly QdrantOptions _options;

    private string CollectionName => _options.Collection ?? "act_section_chunks";

    public QdrantVectorStore(IOptions<QdrantOptions> options)
    {
        _options = options.Value;

        // QdrantClient's (host, port, https, apiKey) constructor -- not a full URL string --
        // so the configured Endpoint has to be parsed first.
        var uri = new Uri(_options.Endpoint);
        _client = new QdrantClient(uri.Host, port: uri.Port, https: uri.Scheme == "https",
            apiKey: _options.ApiKey);
    }

    // Called once at app startup (Program.cs, next to the seed methods) so the collection
    // exists before any UpsertAsync/SearchAsync call reaches it.
    public async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        var exists = await _client.CollectionExistsAsync(CollectionName, cancellationToken);
        if (exists)
        {
            return;
        }

        await _client.CreateCollectionAsync(
            CollectionName,
            new VectorParams { Size = _options.VectorSize, Distance = Distance.Cosine },
            cancellationToken: cancellationToken);
    }

    public async Task UpsertAsync(string vectorId, float[] embedding, Dictionary<string, string> payload)
    {
        var point = new PointStruct
        {
            Id = new PointId { Uuid = vectorId },
            Vectors = embedding,
        };
        foreach (var (key, value) in payload)
        {
            point.Payload[key] = value;
        }

        await _client.UpsertAsync(CollectionName, new[] { point });
    }

    public async Task<IEnumerable<VectorSearchResult>> SearchAsync(float[] queryVector, int topK)
    {
        // SearchAsync is deprecated in Qdrant.Client 1.19.0 in favor of QueryAsync
        // (per-day-1 spike note in plans/Tultul_plan.md) -- same result shape, so this
        // is a drop-in swap.
        var results = await _client.QueryAsync(CollectionName, query: queryVector, limit: (ulong)topK);

        return results.Select(r => new VectorSearchResult(
            r.Id.Uuid,
            r.Score,
            r.Payload.ToDictionary(kv => kv.Key, kv => kv.Value.StringValue)));
    }

    public async Task DeleteAsync(string vectorId)
    {
        await _client.DeleteAsync(CollectionName, new PointId { Uuid = vectorId });
    }
}
