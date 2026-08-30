namespace MuktoAin.Infrastructure.VectorStore;

public class QdrantOptions
{
    public string Endpoint { get; set; } = "https://your-cluster-id.cloud.qdrant.io:6333";
    public string ApiKey { get; set; } = "";

    // Per-developer namespace: locally each teammate uses "act_section_chunks_<name>"
    // so four independent local SQL Server IDENTITY sequences don't corrupt one shared
    // collection. The canonical "act_section_chunks" collection is written only by the
    // single EmbeddingBatchJob run against the merged database. Falls back to the
    // canonical name when unset.
    public string? Collection { get; set; }

    // gemini-embedding-001 output dimension. Confirm against the live GeminiEmbeddingService
    // (S-1.4) before the real EmbeddingBatchJob run -- kept here as config, not a hardcoded
    // const, so a wrong guess is a one-line appsettings fix, not a code change.
    public uint VectorSize { get; set; } = 3072;
}
