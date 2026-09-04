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

    // Embedding output dimension. MUST match Gemini:EmbeddingOutputDimensionality
    // (when set) or the model's default (3072 for gemini-embedding-001).
    // QdrantVectorStore auto-recreates the collection on a dimension mismatch,
    // so changing this is a one-line appsettings fix.
    public uint VectorSize { get; set; } = 3072;
}
