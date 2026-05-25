// =============================================================================
// Graph/GraphEdge.cs — 图边（一次调用关系 A → B）
// =============================================================================
// v2: 新增 Confidence / Evidence / Source / PropagationDepth / Grounded 字段
//     所有边必须携带真实性约束。
// =============================================================================

namespace Core.Graph;

public class GraphEdge
{
    public string FromId { get; set; } = "";

    public string ToId { get; set; } = "";

    /// <summary>边上显示的调用文本（限定名）。</summary>
    public string Call { get; set; } = "";

    /// <summary>目标是否解析到解决方案内部方法。</summary>
    public bool IsResolved { get; set; }

    /// <summary>边类型：call（默认）、nh:entity-access / spring:implements 等。</summary>
    public string Kind { get; set; } = GraphEdgeKinds.Call;

    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.Ordinal);

    // ── v2: Truth / Confidence fields ──

    /// <summary>边的数据来源：Roslyn / NHibernate / SpringNet / AnalyzerInferred / Heuristic。</summary>
    public string Source { get; set; } = EdgeSourceKinds.Roslyn;

    /// <summary>置信度：Exact / High / Medium / Low。</summary>
    public string Confidence { get; set; } = EdgeConfidenceKinds.High;

    /// <summary>证据强度：SemanticDirect / SemanticInferred / SyntaxDirect / SyntaxPattern / Inferred。</summary>
    public string Evidence { get; set; } = EdgeEvidenceKinds.SyntaxDirect;

    /// <summary>从 source fact 出发的传播深度 (0 = 直接证据边)。</summary>
    public int PropagationDepth { get; set; }

    /// <summary>两端节点是否都有关联的源文件/符号。</summary>
    public bool Grounded { get; set; } = true;

    // ── v3: Dependency edge type classification ──

    /// <summary>依赖边分类：DirectCall / TransitiveCall / EntryPointReachable / PrivateImplementation / InterfaceContract。</summary>
    public string DependencyType { get; set; } = DependencyEdgeTypes.DirectCall;
}

public static class GraphEdgeKinds
{
    public const string Call = "call";
}

public static class DependencyEdgeTypes
{
    public const string DirectCall = "direct-call";
    public const string TransitiveCall = "transitive-call";
    public const string EntryPointReachable = "entry-point-reachable";
    public const string PrivateImplementation = "private-implementation";
    public const string InterfaceContract = "interface-contract";
}

public static class EdgeLayer
{
    public const string Call = "call";

    public const string Framework = "framework";

    public const string Data = "data";

    public const string Transaction = "transaction";
}

public static class EdgeSourceKinds
{
    public const string Roslyn = "roslyn";
    public const string NHibernate = "nhibernate";
    public const string SpringNet = "spring";
    public const string AnalyzerInferred = "analyzer-inferred";
    public const string Heuristic = "heuristic";
    public const string Unknown = "unknown";
}

public static class EdgeConfidenceKinds
{
    public const string Exact = "exact";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
}

public static class EdgeEvidenceKinds
{
    public const string SemanticDirect = "semantic-direct";
    public const string SemanticInferred = "semantic-inferred";
    public const string SyntaxDirect = "syntax-direct";
    public const string SyntaxPattern = "syntax-pattern";
    public const string Inferred = "inferred";
}
