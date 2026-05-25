// =============================================================================
// Cognition/CodeFix/PatchGenerator.cs — generate and validate patches
// =============================================================================
// Constraints enforced:
//   - Only modify method body
//   - No public API / signature / config / DI changes
//   - Only generate patch/diff, no auto-commit
// =============================================================================

namespace Core.Cognition.CodeFix;

public sealed class PatchGenerator
{
    private readonly PatchOptions _options;

    public PatchGenerator(PatchOptions? options = null)
    {
        _options = options ?? PatchOptions.Default;
    }

    public GeneratedPatch CreatePatch(LocatedSymbol target, string newMethodBody, string? description = null)
    {
        var patch = new GeneratedPatch
        {
            PatchId = $"patch-{DateTime.UtcNow:HHmmss}",
            FilePath = target.SourceFilePath,
            OriginalCode = target.MethodBody,
            ModifiedCode = newMethodBody,
            ChangeDescription = description ?? $"Modified {target.MethodName} in {target.ClassName}",
            LineStart = target.MethodStartLine,
            LineEnd = target.MethodEndLine,
        };

        return patch;
    }

    public PatchValidationResult Validate(GeneratedPatch patch, LocatedSymbol target)
    {
        var violations = new List<string>();

        // 1. No public API modification
        if (target.IsPublicApi && _options.BlockPublicApiModification)
            violations.Add($"REJECTED: {target.MethodName} is a public API method (Controller/EntryPoint). Modification blocked.");

        // 2. Signature unchanged check
        if (!patch.OriginalCode.Contains("(", StringComparison.Ordinal) || !patch.ModifiedCode.Contains("(", StringComparison.Ordinal))
            violations.Add("WARNING: Cannot verify method signature preservation.");

        // 3. Body modification only
        var originalSig = ExtractSignature(patch.OriginalCode);
        var modifiedSig = ExtractSignature(patch.ModifiedCode);
        if (!string.Equals(originalSig, modifiedSig, StringComparison.Ordinal))
            violations.Add($"REJECTED: Method signature changed from '{originalSig}' to '{modifiedSig}'.");

        // 4. Empty modification check
        if (string.IsNullOrWhiteSpace(patch.ModifiedCode))
            violations.Add("REJECTED: Modified code is empty.");

        // 5. No config/DI keywords
        if (_options.DetectConfigChanges)
        {
            var newContent = patch.ModifiedCode.ToLowerInvariant();
            var forbiddenPatterns = new[] {
                "GetObject(", "context.", "ISession", "ISessionFactory",
                "connectionString", "appSettings", "configurationManager",
                "[Dependency]", "[Inject]", "RegisterType", "RegisterInstance" };
            foreach (var pattern in forbiddenPatterns)
            {
                if (newContent.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    violations.Add($"REJECTED: Modified code references '{pattern}' — config/DI changes not allowed.");
            }
        }

        return new PatchValidationResult
        {
            IsValid = !violations.Any(v => v.StartsWith("REJECTED", StringComparison.Ordinal)),
            Violations = violations,
        };
    }

    public void ApplyPatch(GeneratedPatch patch)
    {
        if (!File.Exists(patch.FilePath))
            throw new FileNotFoundException($"Source file not found: {patch.FilePath}");

        var allLines = File.ReadAllLines(patch.FilePath).ToList();
        var startIdx = patch.LineStart - 1;
        var endIdx = patch.LineEnd - 1;

        if (startIdx < 0 || endIdx >= allLines.Count)
            throw new InvalidOperationException($"Line range {patch.LineStart}-{patch.LineEnd} out of file bounds.");

        var newLines = patch.ModifiedCode.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        allLines.RemoveRange(startIdx, endIdx - startIdx + 1);
        allLines.InsertRange(startIdx, newLines);

        File.WriteAllText(patch.FilePath, string.Join("\n", allLines));
    }

    public void RevertPatch(GeneratedPatch patch)
    {
        if (!File.Exists(patch.FilePath)) return;

        var allLines = File.ReadAllLines(patch.FilePath).ToList();
        var startIdx = patch.LineStart - 1;
        var modifiedLineCount = patch.ModifiedCode.Split('\n').Length;
        var endIdx = startIdx + modifiedLineCount - 1;
        endIdx = Math.Min(endIdx, allLines.Count - 1);

        var originalLines = patch.OriginalCode.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        allLines.RemoveRange(startIdx, endIdx - startIdx + 1);
        allLines.InsertRange(startIdx, originalLines);

        File.WriteAllText(patch.FilePath, string.Join("\n", allLines));
    }

    private static string ExtractSignature(string code)
    {
        var firstBrace = code.IndexOf('{');
        return firstBrace > 0 ? code[..firstBrace].TrimEnd() : code;
    }
}

public class PatchOptions
{
    public bool BlockPublicApiModification { get; init; } = true;
    public bool DetectConfigChanges { get; init; } = true;

    public static PatchOptions Default => new();
}

public sealed class PatchValidationResult
{
    public bool IsValid { get; init; }
    public required IReadOnlyList<string> Violations { get; init; }
}
