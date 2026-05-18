// =============================================================================
// QueryUnderstanding/ProjectVocabulary.cs — 项目词库
// =============================================================================
// 从 Entity names / Table names / Route paths / Method names / Class names
// 和 Chunk keywords 自动构建的项目专属词库。
// 支持序列化为 vocabulary.json。
// =============================================================================

using System.Text.Json.Serialization;

namespace Core.QueryUnderstanding;

public sealed class ProjectVocabulary
{
    public string ScanRoot { get; set; } = "";

    public DateTime GeneratedAt { get; set; }

    public int SchemaVersion { get; set; } = 1;

    public List<VocabularyEntry> Entities { get; set; } = new();

    public List<VocabularyEntry> Tables { get; set; } = new();

    public List<VocabularyEntry> Routes { get; set; } = new();

    public List<VocabularyEntry> Classes { get; set; } = new();

    public List<VocabularyEntry> Methods { get; set; } = new();

    public List<NormalizedTerm> NormalizedTerms { get; set; } = new();

    public Dictionary<string, List<string>> AliasGraph { get; set; } = new(StringComparer.Ordinal);

    public SynonymMap Synonyms { get; set; } = new();

    public int TotalTerms =>
        Entities.Count + Tables.Count + Routes.Count + Classes.Count + Methods.Count;
}

public sealed class VocabularyEntry
{
    public string Original { get; set; } = "";

    public string Normalized { get; set; } = "";

    public List<string> Tokens { get; set; } = new();

    public string Kind { get; set; } = "";

    public string? ProjectName { get; set; }

    public string? FilePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Frequency { get; set; }
}

public sealed class NormalizedTerm
{
    public string Original { get; set; } = "";

    public string Normalized { get; set; } = "";

    public string Source { get; set; } = "";

    public List<string> Components { get; set; } = new();
}
