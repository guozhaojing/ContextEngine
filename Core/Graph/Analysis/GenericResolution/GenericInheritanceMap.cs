// =============================================================================
// GenericResolution/GenericInheritanceMap.cs — 泛型继承映射
// =============================================================================
// 扫描所有源码文件中的 class 声明：
//   - 记录 class 的 base types 和 implemented interfaces
//   - 解析泛型类型参数替换关系
//   - 支持多级继承链中的类型参数传递
//
// 示例：
//   class BaseRepository<T> { }
//   class ReagentRepo : BaseRepository<EQA_Reagent> { }
//   → 记录：在 ReagentRepo 作用域中，BaseRepository 的 T = EQA_Reagent
// =============================================================================

using System.Text.RegularExpressions;
using Core.Models;

namespace Core.Graph.Analysis.GenericResolution;

public sealed class GenericInheritanceMap
{
    private readonly Dictionary<string, ClassInheritanceInfo> _classes =
        new(StringComparer.Ordinal);

    private static readonly HashSet<string> ExcludedDirNames =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules" };

    public IReadOnlyDictionary<string, ClassInheritanceInfo> Classes => _classes;

    public int Count => _classes.Count;

    public void Build(IEnumerable<CodeUnit> codeUnits)
    {
        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var unit in codeUnits)
        {
            if (!processedFiles.Add(unit.FilePath))
                continue;
            if (!File.Exists(unit.FilePath))
                continue;

            var content = File.ReadAllText(unit.FilePath);
            ParseFileClasses(content, unit.FilePath);
        }
    }

    public ClassInheritanceInfo? FindClass(string className, string? namespaceName = null)
    {
        if (namespaceName is not null)
        {
            var fullKey = $"{namespaceName}.{className}";
            if (_classes.TryGetValue(fullKey, out var fullInfo))
                return fullInfo;
        }

        _classes.TryGetValue(className, out var info);
        return info;
    }

    public List<GenericBinding> ResolveTypeParameter(ClassInheritanceInfo classInfo, string typeParamName)
    {
        var results = new List<GenericBinding>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        ResolveRecursive(classInfo, typeParamName, results, visited, 0);
        return results;
    }

    private void ResolveRecursive(
        ClassInheritanceInfo classInfo,
        string typeParamName,
        List<GenericBinding> results,
        HashSet<string> visited,
        int depth)
    {
        if (depth > 10 || !visited.Add(classInfo.FullName))
            return;

        // 直接泛型参数绑定
        if (classInfo.GenericParameterBindings.TryGetValue(typeParamName, out var bindings))
        {
            foreach (var binding in bindings)
                results.Add(new GenericBinding
                    { TypeParameter = typeParamName, BoundType = binding, ViaClass = classInfo.FullName, Depth = depth });
        }

        // 通过 base type 的泛型参数传递
        foreach (var baseType in classInfo.BaseTypes)
        {
            if (!baseType.IsGeneric || baseType.TypeArguments.Count == 0)
                continue;

            var baseClassInfo = FindClass(baseType.Name);
            if (baseClassInfo is null || baseClassInfo.TypeParameters.Count == 0)
                continue;

            for (var i = 0; i < baseType.TypeArguments.Count && i < baseClassInfo.TypeParameters.Count; i++)
            {
                var arg = baseType.TypeArguments[i];
                var param = baseClassInfo.TypeParameters[i];

                if (string.Equals(arg, typeParamName, StringComparison.Ordinal))
                {
                    results.Add(new GenericBinding
                        { TypeParameter = typeParamName, BoundType = $"= {param} (pass-through)", ViaClass = classInfo.FullName, Depth = depth });
                }

                // 如果参数本身是另一个泛型变量，继续追踪
                if (arg.StartsWith("T", StringComparison.Ordinal) || arg.Length == 1)
                {
                    if (classInfo.GenericParameterBindings.TryGetValue(arg, out var argBindings))
                    {
                        foreach (var b in argBindings)
                        {
                            if (!string.Equals(b, typeParamName, StringComparison.Ordinal))
                            {
                                ResolveRecursive(classInfo, b, results, visited, depth + 1);
                            }
                        }
                    }
                }
            }

            // 递归到 base type 寻找
            ResolveRecursive(classInfo, typeParamName, results, visited, depth + 1);
        }

        // 检查 parent 类（如果存在）
        if (classInfo.ParentClass is not null)
        {
            var parentInfo = FindClass(classInfo.ParentClass);
            if (parentInfo is not null)
                ResolveRecursive(parentInfo, typeParamName, results, visited, depth + 1);
        }
    }

    /// <summary>获取类型参数在继承链中最终被绑定的具体类型。</summary>
    public string? ResolveConcreteType(ClassInheritanceInfo classInfo, string typeParamName)
    {
        var bindings = ResolveTypeParameter(classInfo, typeParamName);

        var concrete = bindings
            .Where(b => !b.BoundType.Contains("pass-through", StringComparison.OrdinalIgnoreCase)
                        && !b.BoundType.StartsWith("= "))
            .Select(b => b.BoundType)
            .FirstOrDefault();

        return concrete;
    }

    /// <summary>对 class 的每个 base type 泛型参数，从自身 bindings 解析具体类型。</summary>
    public List<ResolvedGenericArgument> ResolveAllArguments(ClassInheritanceInfo classInfo)
    {
        var results = new List<ResolvedGenericArgument>();

        foreach (var baseType in classInfo.BaseTypes)
        {
            if (!baseType.IsGeneric)
                continue;

            var baseClassInfo = FindClass(baseType.Name);
            var parentParams = baseClassInfo?.TypeParameters
                ?? Enumerable.Range(0, baseType.TypeArguments.Count)
                    .Select(_ => "T").ToList();

            for (var i = 0; i < baseType.TypeArguments.Count; i++)
            {
                var arg = baseType.TypeArguments[i];
                var paramName = i < parentParams.Count ? parentParams[i] : $"T{i}";

                // 如果参数是具体类型（非泛型变量）
                if (IsConcreteType(arg))
                {
                    results.Add(new ResolvedGenericArgument
                    {
                        ParameterName = paramName,
                        ConcreteType = arg,
                        SourceBaseType = baseType.FullName,
                        Confidence = GenericResolutionConfidence.Exact
                    });
                }
                else
                {
                    // 通过自身泛型绑定解析
                    var concrete = ResolveConcreteType(classInfo, arg);
                    if (concrete is not null)
                    {
                        results.Add(new ResolvedGenericArgument
                        {
                            ParameterName = paramName,
                            ConcreteType = concrete,
                            SourceBaseType = baseType.FullName,
                            Confidence = GenericResolutionConfidence.High
                        });
                    }
                    else if (classInfo.GenericParameterBindings.TryGetValue(arg, out var direct))
                    {
                        foreach (var dt in direct)
                        {
                            results.Add(new ResolvedGenericArgument
                            {
                                ParameterName = paramName,
                                ConcreteType = dt,
                                SourceBaseType = baseType.FullName,
                                Confidence = GenericResolutionConfidence.Medium
                            });
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>查找某一类型参数在继承链中的最终实体类型。</summary>
    public string? FindEntityForClass(string className, string typeParamName = "T")
    {
        var classInfo = FindClass(className);
        if (classInfo is null)
            return null;

        return ResolveConcreteType(classInfo, typeParamName);
    }

    private static bool IsConcreteType(string typeArg) =>
        !string.IsNullOrEmpty(typeArg)
        && char.IsUpper(typeArg[0])
        && !(typeArg is "T" or "TEntity" or "TKey" or "TValue"
            || (typeArg.Length <= 2 && typeArg.StartsWith("T", StringComparison.Ordinal)))
        && !Regex.IsMatch(typeArg, @"^T\d+$");

    private void ParseFileClasses(string content, string filePath)
    {
        // 使用简单的正则 + 状态机解析 class 声明
        var lines = content.Split('\n');
        int i = 0;
        var currentNs = "";

        while (i < lines.Length)
        {
            var line = lines[i].Trim();
            i++;

            // 跟踪命名空间
            var nsMatch = Regex.Match(line, @"^namespace\s+([\w.]+)");
            if (nsMatch.Success)
                currentNs = nsMatch.Groups[1].Value;

            // 检测 class 声明
            var classMatch = Regex.Match(line,
                @"(?:public\s+|internal\s+|protected\s+|private\s+|static\s+|sealed\s+|abstract\s+|partial\s+)*" +
                @"class\s+(\w+)\s*(?:<\s*([^>]+?)\s*>)?\s*" +
                @"(?::\s*([^{;]+))?");

            if (classMatch.Success)
            {
                var className = classMatch.Groups[1].Value;
                var genericParams = classMatch.Groups[2].Value;
                var inheritance = classMatch.Groups[3].Value;

                var fullName = string.IsNullOrEmpty(currentNs) ? className : $"{currentNs}.{className}";

                if (!_classes.ContainsKey(fullName))
                {
                    var info = new ClassInheritanceInfo
                    {
                        FullName = fullName,
                        Name = className,
                        Namespace = currentNs,
                        SourceFile = NormalizeFilePath(filePath, null)
                    };

                    // 解析泛型参数
                    if (!string.IsNullOrWhiteSpace(genericParams))
                    {
                        info.TypeParameters = genericParams
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim())
                            .ToList();
                    }

                    // 解析继承列表
                    if (!string.IsNullOrWhiteSpace(inheritance))
                    {
                        ParseInheritanceList(inheritance, info);
                    }

                    _classes[fullName] = info;

                    // 跳过类体
                    i = SkipBracedBlock(lines, i);
                }
            }
        }
    }

    private void ParseInheritanceList(string inheritance, ClassInheritanceInfo info)
    {
        var parts = SplitInheritanceParts(inheritance);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var gtMatch = Regex.Match(trimmed, @"^([\w.]+)\s*(?:<([^>]+)>)?");
            if (!gtMatch.Success)
                continue;

            var typeName = gtMatch.Groups[1].Value;
            var typeArgs = gtMatch.Groups[2].Value;

            var baseType = new GenericBaseType
            {
                Name = typeName,
                FullName = trimmed,
                IsGeneric = !string.IsNullOrWhiteSpace(typeArgs)
            };

            if (baseType.IsGeneric)
            {
                baseType.TypeArguments = typeArgs
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .ToList();
            }

            info.BaseTypes.Add(baseType);
        }
    }

    private static List<string> SplitInheritanceParts(string inheritance)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < inheritance.Length; i++)
        {
            var c = inheritance[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(inheritance[start..i]);
                start = i + 1;
            }
        }

        if (start < inheritance.Length)
            parts.Add(inheritance[start..]);

        return parts;
    }

    private static int SkipBracedBlock(string[] lines, int startLine)
    {
        var depth = 0;
        var started = false;

        for (var i = startLine; i < lines.Length; i++)
        {
            foreach (var c in lines[i])
            {
                if (c == '{') { depth++; started = true; }
                else if (c == '}') { depth--; }
            }

            if (started && depth == 0)
                return i + 1;
        }

        return lines.Length;
    }

    private static string NormalizeFilePath(string filePath, string? scanRoot) =>
        scanRoot is null
            ? filePath.Replace('\\', '/')
            : Path.GetRelativePath(scanRoot, filePath).Replace('\\', '/');
}

public sealed class ClassInheritanceInfo
{
    public string FullName { get; set; } = "";

    public string Name { get; set; } = "";

    public string Namespace { get; set; } = "";

    public List<string> TypeParameters { get; set; } = new();

    /// <summary>class 自身的泛型参数绑定（如 class Foo<T> where T : Bar → T → Bar）</summary>
    public Dictionary<string, List<string>> GenericParameterBindings { get; set; } =
        new(StringComparer.Ordinal);

    public List<GenericBaseType> BaseTypes { get; set; } = new();

    public string? ParentClass { get; set; }

    public string? SourceFile { get; set; }
}

public sealed class GenericBaseType
{
    public string Name { get; set; } = "";

    public string FullName { get; set; } = "";

    public bool IsGeneric { get; set; }

    public List<string> TypeArguments { get; set; } = new();
}

public sealed class GenericBinding
{
    public string TypeParameter { get; set; } = "";

    public string BoundType { get; set; } = "";

    public string ViaClass { get; set; } = "";

    public int Depth { get; set; }
}

public sealed class ResolvedGenericArgument
{
    public string ParameterName { get; set; } = "";

    public string ConcreteType { get; set; } = "";

    public string SourceBaseType { get; set; } = "";

    public GenericResolutionConfidence Confidence { get; set; }
}
