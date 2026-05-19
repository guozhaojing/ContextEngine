// =============================================================================
// Graph/GraphNode.cs — 图节点（一个方法/实体/表/外部节点）
// =============================================================================
// 【约定】本类只有数据字段；CalledBy 由 GraphAdjacencyMaterializer 填充；
// Attributes 由 GraphAnalysisMergeService 写入分析注解。
// v2: 新增 SymbolHandle / SourceFile / GroundingKind 以支持语义 grounding。
// =============================================================================

namespace Core.Graph;

public class GraphNode
{
    /// <summary>稳定主键，见 MethodIdBuilder。</summary>
    public string Id { get; set; } = "";

    /// <summary>节点类型：method / entity / table / external。</summary>
    public string Kind { get; set; } = GraphNodeKind.Method;

    /// <summary>显示用短名，如 AuditService.Audit。</summary>
    public string Label { get; set; } = "";

    public string ProjectName { get; set; } = "";

    public string ProjectPath { get; set; } = "";

    public string Namespace { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    /// <summary>参数类型列表，如 ["int", "string"]，用于区分重载。</summary>
    public List<string> ParameterTypes { get; set; } = new();

    /// <summary>true 表示外部库方法或无法纳入解决方案图的节点。</summary>
    public bool IsExternal { get; set; }

    /// <summary>谁调用了我（上游），由边反向物化：B.CalledBy 含 A 表示 A→B。</summary>
    public List<string> CalledBy { get; set; } = new();

    /// <summary>键值扩展，如 aspnet:route、ef:sql（由分析器合并写入）。</summary>
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.Ordinal);

    // ── v2: Semantic Grounding fields ──

    /// <summary>稳定符号引用（Roslyn DocumentationCommentId）。空表示未绑定的语法节点。</summary>
    public string SymbolHandle { get; set; } = "";

    /// <summary>定义所在的源文件绝对路径。</summary>
    public string SourceFile { get; set; } = "";

    /// <summary>grounding 类型：semantic-method / semantic-type / syntax-only / analyzer-inferred。</summary>
    public string GroundingKind { get; set; } = GroundingKindKinds.SyntaxOnly;

    /// <summary>节点真实性：Fact / Inferred / Uncertain / Hallucinated。</summary>
    public string TruthType { get; set; } = TruthTypeKinds.Fact;

    /// <summary>置信度 0~1。</summary>
    public double Confidence { get; set; } = 1.0;
}

public static class GroundingKindKinds
{
    public const string SemanticMethod = "semantic-method";
    public const string SemanticType = "semantic-type";
    public const string SyntaxOnly = "syntax-only";
    public const string AnalyzerInferred = "analyzer-inferred";
    public const string External = "external";
}

public static class TruthTypeKinds
{
    public const string Fact = "fact";
    public const string Inferred = "inferred";
    public const string Uncertain = "uncertain";
    public const string Hallucinated = "hallucinated";
}
