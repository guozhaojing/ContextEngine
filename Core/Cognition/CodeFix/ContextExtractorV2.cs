// =============================================================================
// Cognition/CodeFix/ContextExtractorV2.cs — minimal effective context extraction
// =============================================================================
// Principle: "minimal effective context" — NOT whole-project dump.
// Only: target method + containing class + directly referenced symbols + DTO/enum.
// Goal: reduce hallucination, token overload, irrelevant context pollution.
// =============================================================================

using Core.Graph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Cognition.CodeFix;

public sealed class ContextExtractorV2
{
    private readonly GraphQueryService _graphQuery;
    private readonly V2Options _options;

    public ContextExtractorV2(GraphQueryService graphQuery, V2Options? options = null)
    {
        _graphQuery = graphQuery;
        _options = options ?? V2Options.Default;
    }

    public V2Context Extract(LocatedSymbol target)
    {
        var context = new V2Context
        {
            TargetMethod = new SymbolRef(target.MethodName, target.FullSignature, target.MethodBody),
            ContainingClass = ExtractClassContext(target),
            ReferencedSymbols = ExtractReferencedSymbols(target),
            RequiredTypes = ExtractRequiredTypes(target),
            UsingDirectives = ExtractUsingDirectives(target.SourceFilePath, _options.MaxUsingDirectives),
        };

        context.ContextSizeChars = context.EstimateSize();
        context.ReferencedSymbolCount = context.ReferencedSymbols.Count;

        return context;
    }

    public string FormatForLLM(V2Context context)
    {
        var sb = new System.Text.StringBuilder();

        if (context.UsingDirectives.Count > 0)
        {
            sb.AppendLine("// using directives");
            foreach (var u in context.UsingDirectives)
                sb.AppendLine(u);
            sb.AppendLine();
        }

        if (context.RequiredTypes.Count > 0)
        {
            sb.AppendLine("// Referenced types used in this method:");
            foreach (var t in context.RequiredTypes)
            {
                sb.AppendLine($"// {t.Name}: {t.Definition}");
            }
            sb.AppendLine();
        }

        if (context.ReferencedSymbols.Count > 0)
        {
            sb.AppendLine("// Methods/fields called by this method:");
            foreach (var s in context.ReferencedSymbols)
            {
                sb.AppendLine($"// {s.Name}: {s.Signature}");
            }
            sb.AppendLine();
        }

        if (context.ContainingClass is not null)
        {
            sb.AppendLine($"// Containing class: {context.ContainingClass.Name}");
            sb.AppendLine($"// Namespace: {context.ContainingClass.Namespace}");
            sb.AppendLine();
        }

        sb.AppendLine("// === TARGET METHOD === //");
        sb.AppendLine($"// Modify ONLY the method body between {{ }}");
        sb.AppendLine(context.TargetMethod.Signature);
        sb.AppendLine(context.TargetMethod.Body);

        if (!string.IsNullOrEmpty(context.CompileErrors))
        {
            sb.AppendLine();
            sb.AppendLine("// === PREVIOUS COMPILE ERRORS === //");
            sb.AppendLine(context.CompileErrors);
        }

        return sb.ToString();
    }

    private ClassRef? ExtractClassContext(LocatedSymbol target)
    {
        if (!File.Exists(target.SourceFilePath)) return null;

        try
        {
            var lines = File.ReadAllLines(target.SourceFilePath);
            var ns = ExtractNamespace(lines);
            // Extract class fields and properties that might be relevant
            var fields = ExtractClassFields(lines, target.ClassName);
            var baseTypes = ExtractBaseTypes(target);

            return new ClassRef(target.ClassName, ns ?? target.Namespace, fields, baseTypes);
        }
        catch
        {
            return new ClassRef(target.ClassName, target.Namespace, new List<string>(), new List<string>());
        }
    }

    private List<SymbolRef> ExtractReferencedSymbols(LocatedSymbol target)
    {
        var symbols = new List<SymbolRef>();

        // From graph: callees
        foreach (var callee in target.Callees.Take(_options.MaxReferencedSymbols))
        {
            symbols.Add(new SymbolRef(callee.MethodName, callee.FullSignature, callee.MethodBody));
        }

        // From method body text: extract method calls and field references
        try
        {
            var tree = CSharpSyntaxTree.ParseText(
                $"class _W {{ void _M() {{ {target.MethodBody} }} }}");
            var root = tree.GetCompilationUnitRoot();

            var memberAccesses = root.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Select(m => m.Name.Identifier.Text)
                .Distinct(StringComparer.Ordinal)
                .Take(10);

            foreach (var name in memberAccesses)
            {
                if (!symbols.Any(s => s.Name == name))
                    symbols.Add(new SymbolRef(name, $"(referenced: {name})", ""));
            }
        }
        catch { }

        return symbols;
    }

    private List<TypeRef> ExtractRequiredTypes(LocatedSymbol target)
    {
        var types = new List<TypeRef>();

        // Extract types from parameter list
        foreach (var paramType in target.ParameterTypes)
        {
            var simpleName = paramType.Split('.').Last();
            types.Add(new TypeRef(simpleName, $"Parameter type: {paramType}", TypeKind.Parameter));
        }

        // Extract types used in method body
        try
        {
            var tree = CSharpSyntaxTree.ParseText(
                $"class _W {{ void _M() {{ {target.MethodBody} }} }}");
            var root = tree.GetCompilationUnitRoot();

            var typeNames = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(n => n.Identifier.Text)
                .Where(name => char.IsUpper(name[0]) && name.Length > 1)
                .Distinct(StringComparer.Ordinal)
                .Take(15);

            foreach (var name in typeNames)
            {
                if (!types.Any(t => t.Name == name))
                {
                    var kind = name.Contains("List", StringComparison.Ordinal) || name.Contains("IList", StringComparison.Ordinal)
                        ? TypeKind.Collection
                        : name.EndsWith("Result", StringComparison.Ordinal) || name.EndsWith("Dto", StringComparison.Ordinal) || name.EndsWith("DTO", StringComparison.Ordinal)
                            ? TypeKind.DTO
                            : TypeKind.Unknown;

                    types.Add(new TypeRef(name, $"Referenced type in body: {name}", kind));
                }
            }
        }
        catch { }

        return types;
    }

    private static string? ExtractNamespace(string[] lines)
    {
        foreach (var line in lines.Take(20))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("namespace ", StringComparison.Ordinal))
                return trimmed[10..].TrimEnd(';', ' ').Trim();
        }
        return null;
    }

    private static List<string> ExtractClassFields(string[] lines, string className)
    {
        var fields = new List<string>();
        var inClass = false;
        var braceDepth = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains($"class {className}", StringComparison.Ordinal))
            {
                inClass = true;
                braceDepth = 1;
                continue;
            }
            if (!inClass) continue;

            foreach (var c in line) { if (c == '{') braceDepth++; if (c == '}') braceDepth--; }
            if (braceDepth == 0) break;

            if (braceDepth == 1 && (trimmed.Contains(" get;", StringComparison.Ordinal)
                || trimmed.Contains(" set;", StringComparison.Ordinal)
                || trimmed.StartsWith("private ", StringComparison.Ordinal)
                || trimmed.StartsWith("protected ", StringComparison.Ordinal)
                || trimmed.StartsWith("public ", StringComparison.Ordinal)))
            {
                fields.Add(trimmed);
            }
        }
        return fields.Take(10).ToList();
    }

    private static List<string> ExtractBaseTypes(LocatedSymbol target)
    {
        var bases = new List<string>();
        foreach (var caller in target.Callers.Take(1))
        {
            if (caller.IsPublicApi)
                bases.Add($"Implements API contract from: {caller.ClassName}.{caller.MethodName}");
        }
        return bases;
    }

    private static IReadOnlyList<string> ExtractUsingDirectives(string filePath, int max)
    {
        if (!File.Exists(filePath)) return Array.Empty<string>();
        try
        {
            return File.ReadAllLines(filePath)
                .Take(50)
                .Where(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal))
                .Take(max)
                .Select(l => l.Trim())
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}

public class V2Options
{
    public int MaxReferencedSymbols { get; init; } = 10;
    public int MaxRequiredTypes { get; init; } = 10;
    public int MaxUsingDirectives { get; init; } = 15;
    public int MaxClassFields { get; init; } = 10;

    public static V2Options Default => new();
}

public sealed class V2Context
{
    public required SymbolRef TargetMethod { get; init; }
    public ClassRef? ContainingClass { get; init; }
    public required IReadOnlyList<SymbolRef> ReferencedSymbols { get; init; }
    public required IReadOnlyList<TypeRef> RequiredTypes { get; init; }
    public required IReadOnlyList<string> UsingDirectives { get; init; }
    public string CompileErrors { get; set; } = "";
    public int ContextSizeChars { get; set; }
    public int ReferencedSymbolCount { get; set; }

    public int EstimateSize()
    {
        var size = TargetMethod.Body.Length;
        size += ReferencedSymbols.Sum(s => s.Signature.Length + s.Body.Length);
        size += RequiredTypes.Sum(t => t.Definition.Length);
        size += UsingDirectives.Sum(u => u.Length);
        size += CompileErrors.Length;
        return size;
    }
}

public sealed record SymbolRef(string Name, string Signature, string Body);
public sealed record TypeRef(string Name, string Definition, TypeKind Kind);
public sealed record ClassRef(string Name, string Namespace, IReadOnlyList<string> Fields, IReadOnlyList<string> BaseTypes);

public enum TypeKind
{
    Unknown = 0, Parameter = 1, DTO = 2, Collection = 3, Entity = 4,
}
