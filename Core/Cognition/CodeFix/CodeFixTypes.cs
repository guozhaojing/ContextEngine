// =============================================================================
// Cognition/CodeFix/CodeFixTypes.cs — shared types for code fix pipeline
// =============================================================================
// Determinism: all locator/extractor output is derived from graph analysis.
// Provenance: every fix records source file, original code, and generated patch.
// Replay: CodeFixResult implements IEquatable for regression comparison.
// Grounding: modifications are traceable to specific source locations.
// =============================================================================

using Core.Graph;

namespace Core.Cognition.CodeFix;

public sealed class CodeFixRequest
{
    public string Query { get; init; } = "";
    public string Task { get; init; } = "";
    public string? TargetFilePath { get; init; }
    public string? TargetMethodName { get; init; }
    public int? TargetLine { get; init; }
    public string? RepositoryPath { get; init; }
    public int MaxRetries { get; init; } = 3;
}

public sealed class LocatedSymbol
{
    public required string NodeId { get; init; }
    public required string MethodName { get; init; }
    public required string ClassName { get; init; }
    public required string Namespace { get; init; }
    public required string SourceFilePath { get; init; }
    public int MethodStartLine { get; init; }
    public int MethodEndLine { get; init; }
    public required string MethodBody { get; init; }
    public required string FullSignature { get; init; }
    public bool IsPrivate { get; init; }
    public bool IsPublicApi { get; init; }
    public required IReadOnlyList<string> ParameterTypes { get; init; }
    public List<LocatedSymbol> Callees { get; init; } = new();
    public List<LocatedSymbol> Callers { get; init; } = new();
}

public sealed class MinimalContext
{
    public required string TargetFileName { get; init; }
    public required string TargetMethodBody { get; init; }
    public required string TargetSignature { get; init; }
    public required string TargetClassName { get; init; }
    public required string TargetNamespace { get; init; }
    public required IReadOnlyList<string> UsingDirectives { get; init; }
    public required IReadOnlyList<string> InterfaceMethods { get; init; }
    public required IReadOnlyList<CalleeContext> RelatedMethods { get; init; }
    public string CompileErrors { get; set; } = "";
}

public sealed class CalleeContext
{
    public required string MethodName { get; init; }
    public required string Signature { get; init; }
    public string Summary { get; init; } = "";
}

public enum PatchKind { ModifyExisting, CreateNewFile }

public sealed class GeneratedPatch
{
    public required string PatchId { get; init; }
    public required string FilePath { get; init; }
    public required string OriginalCode { get; init; }
    public required string ModifiedCode { get; init; }
    public string ChangeDescription { get; init; } = "";
    public int LineStart { get; init; }
    public int LineEnd { get; init; }
    public PatchKind Kind { get; init; }
    public string Diff => Kind == PatchKind.CreateNewFile
        ? GenerateNewFileDiff(ModifiedCode)
        : GenerateDiff(OriginalCode, ModifiedCode);

    public bool IsValid => Kind == PatchKind.CreateNewFile
        ? !string.IsNullOrWhiteSpace(ModifiedCode)
        : !string.IsNullOrWhiteSpace(ModifiedCode) && ModifiedCode != OriginalCode;

    private static string GenerateDiff(string original, string modified)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- original");
        sb.AppendLine($"+++ modified");
        foreach (var line in original.Split('\n'))
            sb.AppendLine($"-{line.TrimEnd('\r')}");
        foreach (var line in modified.Split('\n'))
            sb.AppendLine($"+{line.TrimEnd('\r')}");
        return sb.ToString();
    }

    private static string GenerateNewFileDiff(string content)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- /dev/null");
        sb.AppendLine($"+++ new file");
        foreach (var line in content.Split('\n'))
            sb.AppendLine($"+{line.TrimEnd('\r')}");
        return sb.ToString();
    }
}

public sealed class BuildResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public required IReadOnlyList<CompileError> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public string RawOutput { get; init; } = "";
    public double DurationMs { get; init; }
    public bool HasErrors => Errors.Count > 0;
}

public sealed class CompileError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? FilePath { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public bool IsError { get; init; }

    public string ToContextString() =>
        string.IsNullOrEmpty(FilePath)
            ? $"{Code}: {Message}"
            : $"{FilePath}({Line},{Column}): {Code}: {Message}";
}

public sealed class CodeFixResult : IEquatable<CodeFixResult>
{
    public required string FixId { get; init; }
    public string GeneratedAt { get; init; } = "";
    public bool Success { get; init; }
    public int Attempts { get; init; }
    public required IReadOnlyList<GeneratedPatch> Patches { get; init; }
    public BuildResult? FinalBuild { get; init; }
    public required IReadOnlyList<string> RepairHistory { get; init; }
    public string Summary { get; init; } = "";

    public bool Equals(CodeFixResult? other)
    {
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(FixId, other.FixId) && Success == other.Success;
    }
    public override bool Equals(object? obj) => obj is CodeFixResult other && Equals(other);
    public override int GetHashCode() => FixId.GetHashCode(StringComparison.Ordinal);
}
