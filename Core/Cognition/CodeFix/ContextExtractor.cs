// =============================================================================
// Cognition/CodeFix/ContextExtractor.cs — extract minimal necessary context
// =============================================================================
// Principle: "minimal necessary context" — do NOT dump entire project to LLM.
// Only: target method + related methods + interfaces + using directives.
// =============================================================================

namespace Core.Cognition.CodeFix;

public sealed class ContextExtractor
{
    private readonly ContextExtractOptions _options;

    public ContextExtractor(ContextExtractOptions? options = null)
    {
        _options = options ?? ContextExtractOptions.Default;
    }

    public MinimalContext Extract(LocatedSymbol target)
    {
        var usingDirectives = ExtractUsingDirectives(target.SourceFilePath);
        var interfaceMethods = ExtractInterfaceMethods(target);
        var relatedMethods = ExtractRelatedMethods(target);
        var compileErrors = "";

        return new MinimalContext
        {
            TargetFileName = Path.GetFileName(target.SourceFilePath),
            TargetMethodBody = target.MethodBody,
            TargetSignature = target.FullSignature,
            TargetClassName = target.ClassName,
            TargetNamespace = target.Namespace,
            UsingDirectives = usingDirectives,
            InterfaceMethods = interfaceMethods,
            RelatedMethods = relatedMethods,
            CompileErrors = compileErrors,
        };
    }

    public string FormatForLLM(MinimalContext context)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"// File: {context.TargetFileName}");
        sb.AppendLine($"// Namespace: {context.TargetNamespace}");
        sb.AppendLine($"// Class: {context.TargetClassName}");
        sb.AppendLine();

        if (context.UsingDirectives.Count > 0)
        {
            foreach (var u in context.UsingDirectives.Take(20))
                sb.AppendLine(u);
            sb.AppendLine();
        }

        if (context.InterfaceMethods.Count > 0)
        {
            sb.AppendLine("// Interface contracts this method implements:");
            foreach (var im in context.InterfaceMethods)
                sb.AppendLine($"//   {im}");
            sb.AppendLine();
        }

        if (context.RelatedMethods.Count > 0)
        {
            sb.AppendLine("// Related methods called by this method:");
            foreach (var rm in context.RelatedMethods.Take(5))
            {
                sb.AppendLine($"// {rm.MethodName}: {rm.Signature}");
                if (!string.IsNullOrEmpty(rm.Summary))
                    sb.AppendLine($"//   {rm.Summary}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("// TARGET METHOD — modify ONLY the method body below:");
        sb.AppendLine(context.TargetSignature);
        sb.AppendLine(context.TargetMethodBody);

        if (!string.IsNullOrEmpty(context.CompileErrors))
        {
            sb.AppendLine();
            sb.AppendLine("// COMPILE ERRORS from previous attempt:");
            sb.AppendLine(context.CompileErrors);
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> ExtractUsingDirectives(string filePath)
    {
        if (!File.Exists(filePath)) return Array.Empty<string>();
        try
        {
            return File.ReadAllLines(filePath)
                .Take(50)
                .Where(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal))
                .Select(l => l.Trim())
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static IReadOnlyList<string> ExtractInterfaceMethods(LocatedSymbol target)
    {
        var methods = new List<string>();
        // Collect interface methods from related caller context
        foreach (var caller in target.Callers)
        {
            if (caller.IsPublicApi)
            {
                methods.Add($"{caller.FullSignature}");
            }
        }
        return methods;
    }

    private static IReadOnlyList<CalleeContext> ExtractRelatedMethods(LocatedSymbol target)
    {
        return target.Callees
            .Select(c => new CalleeContext
            {
                MethodName = c.MethodName,
                Signature = c.FullSignature,
                Summary = c.IsPrivate ? "(private, same class)" : "",
            })
            .ToList();
    }
}

public class ContextExtractOptions
{
    public int MaxRelatedMethods { get; init; } = 5;
    public int MaxUsingDirectives { get; init; } = 20;

    public static ContextExtractOptions Default => new();
}
