// =============================================================================
// Cognition/CodeFix/PatchGenerator.cs — generate and validate patches
// =============================================================================
// v2: supports ModifyExisting (line-range replace) and CreateNewFile (new .cs file).
// =============================================================================

using System.Text.RegularExpressions;

namespace Core.Cognition.CodeFix;

public sealed class PatchGenerator
{
    private readonly PatchOptions _options;

    public PatchGenerator(PatchOptions? options = null)
    {
        _options = options ?? PatchOptions.Default;
    }

    /// <summary>Detect whether LLM output is a new file or method modification.</summary>
    public static PatchKind DetectKind(string llmOutput)
    {
        var trimmed = llmOutput.Trim();
        // Check for full file pattern: namespace/using + class declaration
        var hasNamespace = trimmed.Contains("namespace ", StringComparison.Ordinal);
        var hasClass = Regex.IsMatch(trimmed, @"\bclass\s+\w+", RegexOptions.None, TimeSpan.FromSeconds(1));
        var hasUsing = trimmed.StartsWith("using ", StringComparison.Ordinal);

        if ((hasNamespace || hasUsing) && hasClass)
            return PatchKind.CreateNewFile;
        return PatchKind.ModifyExisting;
    }

    /// <summary>Extract class name from new file content.</summary>
    public static string? ExtractClassName(string code)
    {
        var match = Regex.Match(code, @"\bclass\s+(\w+)", RegexOptions.None, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Infer a conventional file path for a new class relative to project root.</summary>
    public static string InferFilePath(string className, string projectDir)
    {
        var dir = "Helpers";
        if (className.EndsWith("Controller", StringComparison.Ordinal)) dir = "Controllers";
        else if (className.EndsWith("Service", StringComparison.Ordinal)) dir = "Services";
        else if (className.EndsWith("Repository", StringComparison.Ordinal)) dir = "Repositories";
        else if (className.EndsWith("Middleware", StringComparison.Ordinal) || className.Contains("Middleware")) dir = "Middleware";
        else if (className.EndsWith("Filter", StringComparison.Ordinal) || className.EndsWith("Attribute", StringComparison.Ordinal)) dir = "Filters";
        else if (className.EndsWith("Extension", StringComparison.Ordinal) || className.Contains("Extensions")) dir = "Extensions";
        else if (className.EndsWith("Model", StringComparison.Ordinal) || className.EndsWith("Dto", StringComparison.Ordinal) || className.EndsWith("DTO", StringComparison.Ordinal)) dir = "Models";
        else if (className.EndsWith("Exception", StringComparison.Ordinal)) dir = "Exceptions";
        else if (className.EndsWith("Validator", StringComparison.Ordinal)) dir = "Validators";
        else if (className.EndsWith("Mapper", StringComparison.Ordinal) || className.EndsWith("Converter", StringComparison.Ordinal)) dir = "Mappers";
        else if (className.EndsWith("Handler", StringComparison.Ordinal)) dir = "Handlers";
        else if (className.EndsWith("Provider", StringComparison.Ordinal)) dir = "Providers";
        else if (className.EndsWith("Options", StringComparison.Ordinal) || className.EndsWith("Config", StringComparison.Ordinal) || className.EndsWith("Configuration", StringComparison.Ordinal)) dir = "Config";

        var subDir = Path.Combine(projectDir, dir);
        return Path.Combine(subDir, $"{className}.cs");
    }

    public GeneratedPatch CreatePatch(LocatedSymbol target, string newMethodBody, string? description = null)
    {
        return new GeneratedPatch
        {
            PatchId = $"patch-{DateTime.UtcNow:HHmmss}",
            FilePath = target.SourceFilePath,
            OriginalCode = target.MethodBody,
            ModifiedCode = newMethodBody,
            ChangeDescription = description ?? $"Modified {target.MethodName} in {target.ClassName}",
            LineStart = target.MethodStartLine,
            LineEnd = target.MethodEndLine,
            Kind = PatchKind.ModifyExisting,
        };
    }

    public GeneratedPatch CreateNewFilePatch(string code, string projectDir, string? description = null)
    {
        var className = ExtractClassName(code) ?? "NewClass";
        var filePath = InferFilePath(className, projectDir);

        return new GeneratedPatch
        {
            PatchId = $"newfile-{DateTime.UtcNow:HHmmss}",
            FilePath = filePath,
            OriginalCode = "",
            ModifiedCode = code,
            ChangeDescription = description ?? $"Create new file: {className}.cs",
            LineStart = 0,
            LineEnd = 0,
            Kind = PatchKind.CreateNewFile,
        };
    }

    public PatchValidationResult Validate(GeneratedPatch patch, LocatedSymbol? target)
    {
        if (patch.Kind == PatchKind.CreateNewFile)
            return ValidateNewFile(patch);

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

    private static PatchValidationResult ValidateNewFile(GeneratedPatch patch)
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(patch.ModifiedCode))
            violations.Add("REJECTED: Generated code is empty.");

        if (File.Exists(patch.FilePath))
            violations.Add("WARNING: File already exists, will be overwritten.");

        return new PatchValidationResult
        {
            IsValid = !violations.Any(v => v.StartsWith("REJECTED", StringComparison.Ordinal)),
            Violations = violations,
        };
    }

    public void ApplyPatch(GeneratedPatch patch)
    {
        if (patch.Kind == PatchKind.CreateNewFile)
        {
            ApplyNewFile(patch);
            return;
        }

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

    private static void ApplyNewFile(GeneratedPatch patch)
    {
        var dir = Path.GetDirectoryName(patch.FilePath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(patch.FilePath, patch.ModifiedCode);
    }

    public void RevertPatch(GeneratedPatch patch)
    {
        if (patch.Kind == PatchKind.CreateNewFile)
        {
            if (File.Exists(patch.FilePath))
                File.Delete(patch.FilePath);
            return;
        }

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
