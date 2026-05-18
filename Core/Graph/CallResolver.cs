using Core.Models;

namespace Core.Graph;

internal static class CallResolver
{
    public static string? ResolveTargetNodeId(
        string call,
        CodeUnit source,
        MethodIndex index)
    {
        var normalized = NormalizeCall(call);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var methodName = ExtractMethodName(normalized);
        if (string.IsNullOrEmpty(methodName))
            return null;

        if (TryResolveByQualifiedName(normalized, methodName, source, index, out var targetId))
            return targetId;

        if (TryResolveSameClass(methodName, source, index, out targetId))
            return targetId;

        if (index.TryGetUniqueByMethodName(methodName, out targetId))
            return targetId;

        if (index.TryGetUniqueByMethodNameInProject(source.ProjectName, methodName, out targetId))
            return targetId;

        return null;
    }

    private static bool TryResolveByQualifiedName(
        string call,
        string methodName,
        CodeUnit source,
        MethodIndex index,
        out string? targetId)
    {
        targetId = null;
        if (!call.Contains('.'))
            return false;

        var segments = call.Split('.');
        for (var i = segments.Length - 2; i >= 0; i--)
        {
            var className = segments[i];
            if (index.TryGet(source.ProjectName, className, methodName, out targetId))
                return true;

            if (index.TryGetAny(className, methodName, out targetId))
                return true;
        }

        return false;
    }

    private static bool TryResolveSameClass(
        string methodName,
        CodeUnit source,
        MethodIndex index,
        out string? targetId)
    {
        targetId = null;
        if (methodName.Contains('.'))
            return false;

        return index.TryGet(source.ProjectName, source.ClassName, methodName, out targetId)
            || index.TryGetAny(source.ClassName, methodName, out targetId);
    }

    private static string NormalizeCall(string call)
    {
        var value = call.Trim();
        if (value.StartsWith("this.", StringComparison.Ordinal))
            return value[5..];
        if (value.StartsWith("base.", StringComparison.Ordinal))
            return value[5..];

        return value;
    }

    private static string ExtractMethodName(string call)
    {
        var name = call;
        var paren = name.IndexOf('(');
        if (paren >= 0)
            name = name[..paren];

        var generic = name.IndexOf('<');
        if (generic >= 0)
            name = name[..generic];

        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }
}

internal sealed class MethodIndex
{
    private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _byMethodName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _byProjectAndMethod = new(StringComparer.Ordinal);

    public MethodIndex(IEnumerable<CodeUnit> units)
    {
        foreach (var unit in units)
        {
            var id = CodeGraphBuilder.ToNodeId(unit);
            _exact[Key(unit.ProjectName, unit.ClassName, unit.MethodName)] = id;
            _exact[KeyAny(unit.ClassName, unit.MethodName)] = id;

            if (!string.IsNullOrEmpty(unit.Namespace))
                _exact[KeyNs(unit.Namespace, unit.ClassName, unit.MethodName)] = id;

            AddToList(_byMethodName, unit.MethodName, id);
            AddToList(_byProjectAndMethod, $"{unit.ProjectName}|{unit.MethodName}", id);
        }
    }

    public bool TryGet(string projectName, string className, string methodName, out string? nodeId) =>
        _exact.TryGetValue(Key(projectName, className, methodName), out nodeId);

    public bool TryGetAny(string className, string methodName, out string? nodeId) =>
        _exact.TryGetValue(KeyAny(className, methodName), out nodeId);

    public bool TryGetUniqueByMethodName(string methodName, out string? nodeId)
    {
        nodeId = null;
        if (!_byMethodName.TryGetValue(methodName, out var ids) || ids.Count != 1)
            return false;

        nodeId = ids[0];
        return true;
    }

    public bool TryGetUniqueByMethodNameInProject(string projectName, string methodName, out string? nodeId)
    {
        nodeId = null;
        if (!_byProjectAndMethod.TryGetValue($"{projectName}|{methodName}", out var ids) || ids.Count != 1)
            return false;

        nodeId = ids[0];
        return true;
    }

    private static void AddToList(Dictionary<string, List<string>> map, string key, string nodeId)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        if (!list.Contains(nodeId, StringComparer.Ordinal))
            list.Add(nodeId);
    }

    private static string Key(string project, string className, string methodName) =>
        $"{project}|{className}|{methodName}";

    private static string KeyAny(string className, string methodName) =>
        $"*|{className}|{methodName}";

    private static string KeyNs(string ns, string className, string methodName) =>
        $"*|{ns}.{className}|{methodName}";
}
