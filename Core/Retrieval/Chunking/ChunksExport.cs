namespace Core.Retrieval.Chunking;

public sealed class ChunksExport
{
    public int SchemaVersion => 1;
    public string GeneratedAt { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public int ChunkCount => Chunks.Count;
    public required IReadOnlyList<CodeChunk> Chunks { get; init; }
}
