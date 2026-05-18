using System.Text;
using Core.Graph;
using Core.Graph.Query;

namespace Core.Context;

public sealed class ContextCompression
{
    private readonly GraphQueryService _query;

    public ContextCompression(GraphQueryService query)
    {
        _query = query;
    }

    public string CompressPath(SemanticPath path)
    {
        var sb = new StringBuilder();
        var prevLayer = "";

        for (var i = 0; i < path.NodeIds.Count; i++)
        {
            var node = _query.GetNode(path.NodeIds[i]);
            var label = node?.Label ?? path.NodeIds[i];
            var layer = InferLayer(node);

            if (i == 0)
            {
                sb.Append(FormatNode(label, layer));
            }
            else if (layer != prevLayer && !string.IsNullOrEmpty(prevLayer))
            {
                sb.Append($" \u2192 [{layer}] {FormatNode(label, layer)}");
            }
            else if (i == 1)
            {
                sb.Append($" calls {FormatNode(label, layer)}");
            }
            else
            {
                sb.Append($" chains to {FormatNode(label, layer)}");
            }

            prevLayer = layer;
        }

        return sb.ToString();
    }

    public string CompressCallChain(IReadOnlyList<string> nodeIds, int maxDepth = 4)
    {
        var sb = new StringBuilder();
        var shown = 0;
        var prevLayer = "";

        foreach (var nid in nodeIds.Take(maxDepth + 1))
        {
            var node = _query.GetNode(nid);
            if (node is null) continue;

            var layer = InferLayer(node);

            if (shown == 0)
            {
                sb.Append(FormatNode(node.Label, layer));
            }
            else if (layer != prevLayer)
            {
                sb.Append($" \u2192 [{layer}] {FormatNode(node.Label, layer)}");
            }
            else if (shown == 1)
            {
                sb.Append($" calls {FormatNode(node.Label, layer)}");
            }

            prevLayer = layer;
            shown++;
        }

        if (nodeIds.Count > maxDepth + 1)
            sb.Append($" ... and {nodeIds.Count - maxDepth - 1} more");

        return sb.ToString();
    }

    public string BuildEntityTableSummary(IEnumerable<GraphNode> entityNodes)
    {
        var sb = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in entityNodes)
        {
            var entityClass = node.ClassName ?? ParseEntityClass(node.Id);
            var tableName = node.Attributes.GetValueOrDefault("nh:table", "")
                            ?? ParseTableName(node.Id);

            var key = $"{entityClass}::{tableName}";
            if (!seen.Add(key)) continue;

            if (sb.Length > 0) sb.Append('\n');
            sb.Append($"  {entityClass} \u2192 {tableName}");
        }

        return sb.ToString();
    }

    public string ExtractBusinessRules(string content)
    {
        var sb = new StringBuilder();
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.Contains("Validation", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("Validate", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  \u2022 [Validation] {CleanText(trimmed)}");
            }
            else if (trimmed.Contains("Check", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Contains("Ensure", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Contains("Guard", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  \u2022 [Guard] {CleanText(trimmed)}");
            }
            else if (trimmed.Contains("Requires", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Contains("Must", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Contains("Should", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  \u2022 [Rule] {CleanText(trimmed)}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public string ExtractVariables(IEnumerable<GraphNode> nodes)
    {
        var sb = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            foreach (var param in node.ParameterTypes)
            {
                if (seen.Add(param) && IsMeaningfulType(param))
                    sb.AppendLine($"  \u2022 {param}");
            }

            // Extract from method name (e.g., "FindByStatus" → parameter "status")
            var methodParts = SplitCamelCase(node.MethodName);
            foreach (var part in methodParts.Skip(1)) // skip verb prefix
            {
                if (part.Length > 2 && !IsCommonVerb(part) && seen.Add(part))
                    sb.AppendLine($"  \u2022 {part}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    // ── Helpers ──────────────────────────────────────────────

    private static string FormatNode(string label, string layer)
    {
        var shortLabel = label.Length > 50 ? label[..47] + "..." : label;
        return $"{shortLabel}";
    }

    private static string InferLayer(GraphNode? node)
    {
        if (node is null) return "unknown";
        if (node.Attributes.ContainsKey("aspnet-route:entry-point")) return "route";
        if (node.Kind == "entity") return "entity";
        if (node.Kind == "table") return "table";
        var cn = node.ClassName ?? "";
        if (cn.EndsWith("Controller", StringComparison.Ordinal)) return "controller";
        if (cn.EndsWith("Service", StringComparison.Ordinal)) return "service";
        if (cn.EndsWith("Repository", StringComparison.Ordinal)) return "repository";
        return "method";
    }

    private static string ParseEntityClass(string nodeId)
    {
        var parts = nodeId.Split("::");
        if (parts.Length < 4) return "";
        var entityPart = parts[3];
        var dotIdx = entityPart.LastIndexOf('.');
        return dotIdx >= 0 ? entityPart[(dotIdx + 1)..] : entityPart;
    }

    private static string ParseTableName(string nodeId)
    {
        var parts = nodeId.Split("::");
        return parts.Length >= 5 ? parts[4] : "";
    }

    private static string CleanText(string text)
    {
        return text.Trim().TrimStart('-', '*', '#', ' ').Trim();
    }

    private static bool IsMeaningfulType(string type) =>
        type.Length > 2 && !type.Equals("int", StringComparison.OrdinalIgnoreCase)
                       && !type.Equals("string", StringComparison.OrdinalIgnoreCase)
                       && !type.Equals("bool", StringComparison.OrdinalIgnoreCase)
                       && !type.Equals("void", StringComparison.OrdinalIgnoreCase);

    private static bool IsCommonVerb(string word) =>
        word.Length <= 3;

    private static IEnumerable<string> SplitCamelCase(string? input)
    {
        if (string.IsNullOrEmpty(input)) yield break;
        var start = 0;
        for (var i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]) && (char.IsLower(input[i - 1]) ||
                (i + 1 < input.Length && char.IsLower(input[i + 1]))))
            {
                yield return input[start..i];
                start = i;
            }
        }
        yield return input[start..];
    }
}
