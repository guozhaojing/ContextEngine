// =============================================================================
// Graph/GraphNode.cs — 图节点（一个方法）
// =============================================================================
// 【约定】本类只有数据字段；CalledBy 由 GraphAdjacencyMaterializer 填充；
// Attributes 由 GraphAnalysisMergeService 写入分析注解。
// =============================================================================

namespace Core.Graph;

public class GraphNode
{
    /// <summary>稳定主键，见 MethodIdBuilder。</summary>
    public string Id { get; set; } = "";

    /// <summary>显示用短名，如 AuditService.Audit。</summary>
    public string Label { get; set; } = "";

    public string ProjectName { get; set; } = "";

    public string ProjectPath { get; set; } = "";

    public string Namespace { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    /// <summary>true 表示外部库方法或无法纳入解决方案图的节点。</summary>
    public bool IsExternal { get; set; }

    /// <summary>谁调用了我（上游），由边反向物化：B.CalledBy 含 A 表示 A→B。</summary>
    public List<string> CalledBy { get; set; } = new();

    /// <summary>键值扩展，如 aspnet:route、ef:sql（由分析器合并写入）。</summary>
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.Ordinal);
}
