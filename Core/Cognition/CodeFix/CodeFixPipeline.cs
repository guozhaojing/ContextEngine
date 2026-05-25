// =============================================================================
// Cognition/CodeFix/CodeFixPipeline.cs — locate→context→patch→build→retry
// =============================================================================
// Determinism: locate and extract are deterministic; LLM generation is NOT.
// Provenance: every attempt records patch diff, build result, and repair history.
// Replay: CodeFixResult is comparable for regression testing.
// Grounding: modifications are traceable to specific source locations.
// =============================================================================

using Core.Cognition.SemanticDoc;
using Core.Graph;

namespace Core.Cognition.CodeFix;

public sealed class CodeFixPipeline
{
    private readonly SymbolLocator _locator;
    private readonly ContextExtractor _extractor;
    private readonly PatchGenerator _patchGen;
    private readonly BuildValidator _buildValidator;
    private readonly PipelineOptions _options;

    public CodeFixPipeline(
        GraphQueryService graphQuery,
        PipelineOptions? options = null,
        SemanticEmbeddingService? semanticSearch = null)
    {
        _locator = new SymbolLocator(graphQuery, semanticSearch);
        _extractor = new ContextExtractor();
        _patchGen = new PatchGenerator();
        _buildValidator = new BuildValidator();
        _options = options ?? PipelineOptions.Default;
    }

    public async Task<CodeFixResult> ExecuteAsync(
        CodeFixRequest request,
        Func<string, Task<string>> llmGenerator)
    {
        var fixId = $"fix-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var repairHistory = new List<string>();
        var allPatches = new List<GeneratedPatch>();

        // Step 1: Locate
        var symbols = _locator.Locate(request);
        if (symbols.Count == 0)
        {
            return new CodeFixResult
            {
                FixId = fixId,
                GeneratedAt = DateTime.UtcNow.ToString("O"),
                Success = false,
                Attempts = 0,
                Patches = Array.Empty<GeneratedPatch>(),
                RepairHistory = new[] { "No matching symbols found in code graph." },
                Summary = "Could not locate target method.",
            };
        }

        var target = symbols[0];

        // Block public API
        if (target.IsPublicApi && _options.BlockPublicApiModification)
        {
            return new CodeFixResult
            {
                FixId = fixId,
                GeneratedAt = DateTime.UtcNow.ToString("O"),
                Success = false,
                Attempts = 0,
                Patches = Array.Empty<GeneratedPatch>(),
                RepairHistory = new[] { $"Target {target.MethodName} is a public API. Modification blocked for safety." },
                Summary = $"Cannot modify public API method '{target.MethodName}' in Controller/EntryPoint.",
            };
        }

        // Step 2: Extract context
        var context = _extractor.Extract(target);
        var contextText = _extractor.FormatForLLM(context);
        var taskPrompt = BuildTaskPrompt(request, contextText);

        // Step 3-5: Generate → Validate → Build → Retry loop
        var maxRetries = request.MaxRetries > 0 ? request.MaxRetries : _options.MaxRetries;
        BuildResult? lastBuild = null;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            repairHistory.Add($"Attempt {attempt}/{maxRetries}");

            // Generate prompt (include previous errors if any)
            var prompt = attempt == 1
                ? taskPrompt
                : taskPrompt + "\n\n// PREVIOUS BUILD FAILED. Fix these errors:\n" + context.CompileErrors;

            // LLM generates new method body
            string newBody;
            try
            {
                newBody = await llmGenerator(prompt);
            }
            catch (Exception ex)
            {
                repairHistory.Add($"LLM generation failed: {ex.Message}");
                break;
            }

            // Create and validate patch
            var patch = _patchGen.CreatePatch(target, newBody, $"Attempt {attempt}");
            var validation = _patchGen.Validate(patch, target);

            if (!validation.IsValid)
            {
                repairHistory.AddRange(validation.Violations);
                if (validation.Violations.Any(v => v.StartsWith("REJECTED", StringComparison.Ordinal)))
                    break; // Fatal violation — don't retry
                continue;
            }

            allPatches.Add(patch);

            // Apply patch to disk
            _patchGen.ApplyPatch(patch);

            // Build
            var projectPath = _options.ProjectPath ?? Path.GetDirectoryName(target.SourceFilePath) ?? "";
            lastBuild = await _buildValidator.BuildAsync(projectPath);

            if (lastBuild.Success)
            {
                repairHistory.Add($"Build SUCCESS after {attempt} attempt(s).");
                return new CodeFixResult
                {
                    FixId = fixId,
                    GeneratedAt = DateTime.UtcNow.ToString("O"),
                    Success = true,
                    Attempts = attempt,
                    Patches = allPatches,
                    FinalBuild = lastBuild,
                    RepairHistory = repairHistory,
                    Summary = $"Successfully modified {target.MethodName} in {target.ClassName}. Build passed.",
                };
            }

            // Build failed — collect errors for retry context
            var errorContext = string.Join("\n",
                lastBuild.Errors.Select(e => e.ToContextString()));
            context.CompileErrors = errorContext;
            repairHistory.Add($"Build FAILED: {lastBuild.Errors.Count} error(s).");

            // Revert patch for next attempt
            _patchGen.RevertPatch(patch);
        }

        // All attempts exhausted
        return new CodeFixResult
        {
            FixId = fixId,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            Success = false,
            Attempts = maxRetries,
            Patches = allPatches,
            FinalBuild = lastBuild,
            RepairHistory = repairHistory,
            Summary = $"Failed after {maxRetries} attempts. Last build had {lastBuild?.Errors.Count ?? 0} error(s).",
        };
    }

    private static string BuildTaskPrompt(CodeFixRequest request, string contextText)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a C# code fixer. Your ONLY job is to modify the method body below.");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("1. DO NOT change the method signature (name, parameters, return type).");
        sb.AppendLine("2. DO NOT add new using directives.");
        sb.AppendLine("3. DO NOT modify any code OUTSIDE the method body.");
        sb.AppendLine("4. Respond ONLY with the new method body (including the signature line and braces).");
        sb.AppendLine("5. Keep existing logging patterns (ILogger, Debug, Info).");
        sb.AppendLine($"6. Task: {request.Task}");
        sb.AppendLine();
        sb.AppendLine(contextText);
        return sb.ToString();
    }
}

public class PipelineOptions
{
    public int MaxRetries { get; init; } = 3;
    public bool BlockPublicApiModification { get; init; } = true;
    public string? ProjectPath { get; init; }

    public static PipelineOptions Default => new();
}
