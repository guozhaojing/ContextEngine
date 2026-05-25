// =============================================================================
// Cognition/CodeFix/BuildValidator.cs — dotnet build + error collection
// =============================================================================
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Core.Cognition.CodeFix;

public sealed class BuildValidator
{
    private readonly BuildOptions _options;

    public BuildValidator(BuildOptions? options = null)
    {
        _options = options ?? BuildOptions.Default;
    }

    public async Task<BuildResult> BuildAsync(string projectPath)
    {
        var errors = new List<CompileError>();
        var warnings = new List<string>();
        var output = new System.Text.StringBuilder();
        var sw = Stopwatch.StartNew();

        try
        {
            var csprojPath = ResolveProjectPath(projectPath);
            if (csprojPath is null)
            {
                return new BuildResult
                {
                    Success = false,
                    ExitCode = -1,
                    Errors = new[] { new CompileError { Code = "CE000", Message = $"No .csproj found under: {projectPath}", IsError = true } },
                    Warnings = Array.Empty<string>(),
                    RawOutput = "Project not found.",
                };
            }

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{csprojPath}\" --no-restore -v q",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                throw new InvalidOperationException("Failed to start dotnet build.");

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            sw.Stop();

            output.AppendLine(stdout);
            output.AppendLine(stderr);

            ParseBuildOutput(stdout + "\n" + stderr, errors, warnings);

            return new BuildResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Errors = errors,
                Warnings = warnings,
                RawOutput = output.ToString(),
                DurationMs = sw.Elapsed.TotalMilliseconds,
            };
        }
        catch (Exception ex)
        {
            return new BuildResult
            {
                Success = false,
                ExitCode = -1,
                Errors = new[] { new CompileError { Code = "CE999", Message = ex.Message, IsError = true } },
                Warnings = Array.Empty<string>(),
                RawOutput = output.ToString(),
            };
        }
    }

    private static string? ResolveProjectPath(string path)
    {
        if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return path;

        if (Directory.Exists(path))
        {
            var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                         && !f.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
                .Take(1)
                .ToList();

            if (csprojFiles.Count > 0) return csprojFiles[0];
        }

        return null;
    }

    private static void ParseBuildOutput(string output, List<CompileError> errors, List<string> warnings)
    {
        // MSBuild error format: file.cs(line,col): error CS0000: message
        var errorRegex = new Regex(
            @"^(?<file>[^\(]+)\((?<line>\d+),(?<col>\d+)\):\s*(?<severity>error|warning)\s+(?<code>[A-Z]+\d+):\s*(?<message>.+)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        foreach (Match match in errorRegex.Matches(output))
        {
            var isError = match.Groups["severity"].Value == "error";
            var entry = new CompileError
            {
                FilePath = match.Groups["file"].Value.Trim(),
                Line = int.TryParse(match.Groups["line"].Value, out var l) ? l : 0,
                Column = int.TryParse(match.Groups["col"].Value, out var c) ? c : 0,
                Code = match.Groups["code"].Value,
                Message = match.Groups["message"].Value.Trim(),
                IsError = isError,
            };

            if (isError)
                errors.Add(entry);
            else
                warnings.Add(entry.ToContextString());
        }

        // Fallback: catch any generic error lines
        if (errors.Count == 0 && warnings.Count == 0 && output.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new CompileError
            {
                Code = "CE000",
                Message = output.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
                    ? "Build failed — check project configuration."
                    : "Build had errors (parse failed).",
                IsError = true,
            });
        }
    }
}

public class BuildOptions
{
    public int BuildTimeoutMs { get; init; } = 120_000;
    public static BuildOptions Default => new();
}
