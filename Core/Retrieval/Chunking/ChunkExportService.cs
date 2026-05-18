using System.Text.Json;
using Core.Graph;
using Core.Retrieval.Ranking;

namespace Core.Retrieval.Chunking;

public sealed class ChunkExportService
{
    private readonly ChunkBuilder _builder;
    private readonly RetrievalMetadataBuilder _metadataBuilder;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChunkExportService(GraphQueryService query)
    {
        _builder = new ChunkBuilder(query);
        _metadataBuilder = new RetrievalMetadataBuilder(query);
    }

    public ChunksExport ExportAll()
    {
        var rawChunks = _builder.BuildAll();
        var enriched = _metadataBuilder.Enrich(rawChunks);
        return new ChunksExport { Chunks = enriched };
    }

    public async Task<string> SaveAsync(
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        outputDirectory ??= Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        var export = ExportAll();
        var fileName = "chunks.json";
        var outputPath = Path.Combine(outputDirectory, fileName);

        var json = JsonSerializer.Serialize(export, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        return Path.GetFullPath(outputPath);
    }
}
