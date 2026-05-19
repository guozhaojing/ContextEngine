// =============================================================================
// GenericResolution/GenericInheritanceMap.cs — 泛型继承映射 (Roslyn SyntaxTree)
// =============================================================================
// 使用 Roslyn SyntaxTree 扫描所有源码文件中的 class/interface 声明：
//   - 记录 class 的 base types 和 implemented interfaces
//   - 解析泛型类型参数替换关系
//   - 支持多级继承链中的类型参数传递
//   - 完全替代 regex-based parsing
//
// Roslyn 优势：
//   - 正确处理 attributes / modifiers / partial class / nested class
//   - 正确处理 file-scoped namespace
//   - 正确处理 multiline declarations
//   - 正确处理泛型约束 (where T : class)
// =============================================================================

using Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    public void BuildFromFiles(IEnumerable<string> filePaths)
    {
        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileList = filePaths.ToList();
        var totalFiles = fileList.Count;
        var readCount = 0;
        var excludedCount = 0;

        foreach (var filePath in fileList)
        {
            if (!processedFiles.Add(filePath))
                continue;
            if (!File.Exists(filePath))
                continue;
            if (ShouldExcludeFile(filePath))
            {
                excludedCount++;
                continue;
            }

            try
            {
                var content = File.ReadAllText(filePath);
                ParseFileClasses(content, filePath);
                readCount++;
            }
            catch { }
        }

        Console.WriteLine($"  [InheritanceMap] Total files: {totalFiles}, Read: {readCount}, Excluded: {excludedCount}, Classes found: {_classes.Count}");
    }

    public void BuildFromSyntaxTrees(IEnumerable<(string FilePath, SyntaxTree Tree)> syntaxTrees)
    {
        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileList = syntaxTrees.ToList();
        var totalFiles = fileList.Count;
        var readCount = 0;
        var excludedCount = 0;

        foreach (var (filePath, tree) in fileList)
        {
            if (!processedFiles.Add(filePath))
                continue;
            if (ShouldExcludeFile(filePath))
            {
                excludedCount++;
                continue;
            }

            try
            {
                ParseSyntaxTree(tree, filePath);
                readCount++;
            }
            catch { }
        }

        Console.WriteLine($"  [InheritanceMap] Total files: {totalFiles}, Read: {readCount}, Excluded: {excludedCount}, Classes found: {_classes.Count}");
    }

    private static bool ShouldExcludeFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)) return true;

        var dir = Path.GetDirectoryName(filePath);
        if (dir is not null)
        {
            foreach (var part in dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (ExcludedDirNames.Contains(part))
                    return true;
            }
        }

        return false;
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

        if (classInfo.GenericParameterBindings.TryGetValue(typeParamName, out var bindings))
        {
            foreach (var binding in bindings)
                results.Add(new GenericBinding
                    { TypeParameter = typeParamName, BoundType = binding, ViaClass = classInfo.FullName, Depth = depth });
        }

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

            ResolveRecursive(classInfo, typeParamName, results, visited, depth + 1);
        }

        if (classInfo.ParentClass is not null)
        {
            var parentInfo = FindClass(classInfo.ParentClass);
            if (parentInfo is not null)
                ResolveRecursive(parentInfo, typeParamName, results, visited, depth + 1);
        }
    }

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
        && !NamePatterns.IsGenericParameter(typeArg);

    // =========================================================================
    // SyntaxTree-based parsing (replaces all regex)
    // =========================================================================

    private void ParseFileClasses(string content, string filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(content);
        ParseSyntaxTree(tree, filePath);
    }

    private void ParseSyntaxTree(SyntaxTree tree, string filePath)
    {
        var root = tree.GetCompilationUnitRoot();

        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (typeDecl is not (ClassDeclarationSyntax or InterfaceDeclarationSyntax))
                continue;

            ProcessTypeDeclaration(typeDecl, filePath, isNested: false);
        }
    }

    private void ProcessTypeDeclaration(
        TypeDeclarationSyntax typeDecl,
        string filePath,
        bool isNested)
    {
        var className = typeDecl.Identifier.Text;
        var ns = ResolveNamespace(typeDecl);
        var fullName = string.IsNullOrEmpty(ns) ? className : $"{ns}.{className}";

        var nestedTypes = typeDecl.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(t => t.Parent == typeDecl)
            .ToList();

        foreach (var nested in nestedTypes)
        {
            ProcessTypeDeclaration(nested, filePath, isNested: true);
        }

        if (_classes.ContainsKey(fullName))
        {
            var existing = _classes[fullName];
            if (typeDecl.BaseList is not null)
            {
                foreach (var bt in ParseBaseTypes(typeDecl.BaseList))
                {
                    if (!existing.BaseTypes.Any(b =>
                        string.Equals(b.FullName, bt.FullName, StringComparison.Ordinal)))
                    {
                        existing.BaseTypes.Add(bt);
                    }
                }
            }

            if (typeDecl.TypeParameterList is not null && existing.TypeParameters.Count == 0)
            {
                existing.TypeParameters = typeDecl.TypeParameterList.Parameters
                    .Select(p => p.Identifier.Text)
                    .ToList();
            }

            if (existing.ParentClass is null && isNested && typeDecl.Parent is TypeDeclarationSyntax parentDecl)
            {
                var parentNs = ResolveNamespace(parentDecl);
                var parentName = parentDecl.Identifier.Text;
                existing.ParentClass = string.IsNullOrEmpty(parentNs)
                    ? parentName
                    : $"{parentNs}.{parentName}";
            }

            return;
        }

        var info = new ClassInheritanceInfo
        {
            FullName = fullName,
            Name = className,
            Namespace = ns,
            SourceFile = NormalizeFilePath(filePath, null)
        };

        if (typeDecl.TypeParameterList is not null)
        {
            info.TypeParameters = typeDecl.TypeParameterList.Parameters
                .Select(p => p.Identifier.Text)
                .ToList();
        }

        if (typeDecl.BaseList is not null)
        {
            info.BaseTypes = ParseBaseTypes(typeDecl.BaseList);
        }

        if (isNested && typeDecl.Parent is TypeDeclarationSyntax parentTypeDecl)
        {
            var parentNs = ResolveNamespace(parentTypeDecl);
            var parentName = parentTypeDecl.Identifier.Text;
            info.ParentClass = string.IsNullOrEmpty(parentNs)
                ? parentName
                : $"{parentNs}.{parentName}";
        }

        _classes[fullName] = info;
    }

    private static List<GenericBaseType> ParseBaseTypes(BaseListSyntax baseList)
    {
        var results = new List<GenericBaseType>();

        foreach (var baseTypeSyntax in baseList.Types)
        {
            var type = baseTypeSyntax.Type;
            var typeName = ExtractTypeSimpleName(type);
            var fullName = type.ToString();
            var typeArgs = new List<string>();

            if (type is GenericNameSyntax genericName)
            {
                typeArgs = genericName.TypeArgumentList.Arguments
                    .Select(a => a.ToString().Trim())
                    .ToList();
            }

            results.Add(new GenericBaseType
            {
                Name = typeName,
                FullName = fullName,
                IsGeneric = typeArgs.Count > 0,
                TypeArguments = typeArgs
            });
        }

        return results;
    }

    private static string ExtractTypeSimpleName(TypeSyntax type)
    {
        return type switch
        {
            PredefinedTypeSyntax predefined => predefined.Keyword.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            NullableTypeSyntax nullable => ExtractTypeSimpleName(nullable.ElementType),
            ArrayTypeSyntax array => ExtractTypeSimpleName(array.ElementType),
            _ => type.ToString().Split('<', '>', '[', ']')[0]
        };
    }

    private static string ResolveNamespace(SyntaxNode node)
    {
        var nsParts = node.Ancestors()
            .SelectMany(a =>
            {
                if (a is BaseNamespaceDeclarationSyntax nsDecl)
                    return new[] { nsDecl.Name.ToString() };
                return Array.Empty<string>();
            })
            .Reverse()
            .ToList();

        return string.Join(".", nsParts);
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
