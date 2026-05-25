// =============================================================================
// SemanticDoc/SemanticDocBuilder.cs — builds semantic docs + reverse index
// =============================================================================
// Purpose: Extract structured semantic knowledge from every method.
// Method: Lightweight Roslyn SyntaxWalker for structured info + regex for SQL/URL.
// Output: MethodSemanticDoc per method + ReverseIndex for exact lookup.
// Performance: 2500 methods < 5 seconds (no SemanticModel, no binding).
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Core.Graph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Cognition.SemanticDoc;

public sealed class SemanticDocBuilder
{
    private readonly GraphQueryService _graphQuery;
    private readonly DocBuilderOptions _options;

    public SemanticDocBuilder(GraphQueryService graphQuery, DocBuilderOptions? options = null)
    {
        _graphQuery = graphQuery;
        _options = options ?? DocBuilderOptions.Default;
    }

    public SemanticDocResult BuildAll()
    {
        var result = new SemanticDocResult();
        var allNodes = _graphQuery.GetAllNodes()
            .Where(n => !n.IsExternal && !string.IsNullOrEmpty(n.SourceFile) && File.Exists(n.SourceFile))
            .OrderBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var node in allNodes)
        {
            var doc = BuildOne(node);
            if (doc is not null)
            {
                result.Docs.Add(doc);
                AddToReverseIndex(result.ReverseIndex, doc, node.ProjectName);
            }
        }

        return result;
    }

    private MethodSemanticDoc? BuildOne(GraphNode node)
    {
        try
        {
            var source = File.ReadAllText(node.SourceFile);
            var tree = CSharpSyntaxTree.ParseText(source, path: node.SourceFile);
            var root = tree.GetCompilationUnitRoot();

            // Find the method body in the syntax tree
            var methodDecl = FindMethod(root, node.MethodName, node.ClassName);
            var methodBody = methodDecl?.Body?.ToString()
                ?? methodDecl?.ExpressionBody?.ToString()
                ?? "";

            // Walk the method body for structured extraction
            var walker = new MethodBodyWalker();
            if (methodDecl?.Body is not null)
                walker.Visit(methodDecl.Body);
            else if (methodDecl?.ExpressionBody is not null)
                walker.Visit(methodDecl.ExpressionBody);

            // Extract with regex from string literals
            var sqlTables = ExtractSqlTables(walker.StringLiterals);
            var httpUrls = ExtractHttpUrls(walker.StringLiterals);
            var filePaths = ExtractFilePaths(walker.StringLiterals);
            var configKeys = ExtractConfigKeys(walker.StringLiterals);

            // Graph-derived data
            var calleeIds = _graphQuery.GetCallees(node.Id);
            var callerIds = _graphQuery.GetCallers(node.Id);
            var calleeNames = calleeIds
                .Select(id => _graphQuery.GetNode(id)?.MethodName ?? ShortenId(id))
                .Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).Take(10).ToList();
            var callerNames = callerIds
                .Select(id => _graphQuery.GetNode(id)?.MethodName ?? ShortenId(id))
                .Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).Take(5).ToList();

            // Content hash for cache invalidation
            var contentHash = ComputeHash(methodBody);

            return new MethodSemanticDoc
            {
                MethodId = node.Id,
                MethodName = node.MethodName,
                ClassName = node.ClassName ?? "",
                Namespace = node.Namespace,
                ProjectPath = node.ProjectPath,
                SourceFile = node.SourceFile,
                CalledMethods = calleeNames.Select(n => n!).ToList(),
                CallerMethods = callerNames.Select(n => n!).ToList(),
                SqlTables = sqlTables,
                HttpUrls = httpUrls,
                ExceptionTypes = walker.ExceptionTypes.Distinct(StringComparer.Ordinal).Take(5).ToList(),
                DtoTypes = walker.DtoTypes.Distinct(StringComparer.Ordinal).Take(5).ToList(),
                FilePaths = filePaths,
                ConfigKeys = configKeys,
                ContentHash = contentHash,
            };
        }
        catch
        {
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Syntax tree helpers
    // ═══════════════════════════════════════════════════════════════

    private static MethodDeclarationSyntax? FindMethod(SyntaxNode root, string methodName, string className)
    {
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (classDecl.Identifier.Text == className)
            {
                return classDecl.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == methodName);
            }
        }
        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName);
    }

    // ═══════════════════════════════════════════════════════════════
    // Regex extraction from string literals
    // ═══════════════════════════════════════════════════════════════

    private static readonly Regex SqlTableRegex = new(
        @"(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM|FROM|JOIN)\s+[\[`""]?(\w+)[\]`""]?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex HqlEntityRegex = new(
        @"\b([A-Z][a-z]+){2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HttpUrlRegex = new(
        @"(?:https?://[^\s""']+|""(?:/api/[^""]+)""|' (?:/api/[^']+)'|HttpGet\(@?""([^""]+)""\)|HttpPost\(@?""([^""]+)""\)|Route\(@?""([^""]+)""\))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FilePathRegex = new(
        @"[""'](?:[A-Za-z]:\\|\.\.?[/\\]|~[/\\])[^""']*\.[a-z]{2,4}[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ConfigKeyRegex = new(
        @"(?:Configuration\[""([^""]+)""\]|ConfigHelper\.\w+\(@?""([^""]+)""\)|AppSettings\[""([^""]+)""\]|GetConfigString\(@?""([^""]+)""\))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<string> ExtractSqlTables(List<string> literals)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lit in literals.Take(50))
        {
            foreach (Match m in SqlTableRegex.Matches(lit))
            {
                var table = m.Groups[1].Value;
                if (table.Length > 1 && !IsKeyword(table))
                    tables.Add(table);
            }
            foreach (Match m in HqlEntityRegex.Matches(lit))
            {
                var entity = m.Value;
                if (entity.Length > 3 && entity.Any(char.IsLower) && entity.Any(char.IsUpper))
                    tables.Add(entity);
            }
        }
        return tables.OrderBy(t => t, StringComparer.Ordinal).Take(10).ToList();
    }

    private static List<string> ExtractHttpUrls(List<string> literals)
    {
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lit in literals.Take(30))
        {
            foreach (Match m in HttpUrlRegex.Matches(lit))
            {
                for (var i = 1; i < m.Groups.Count; i++)
                    if (m.Groups[i].Success && m.Groups[i].Value.Length > 1)
                        urls.Add(m.Groups[i].Value);
            }
        }
        return urls.OrderBy(u => u, StringComparer.Ordinal).Take(8).ToList();
    }

    private static List<string> ExtractFilePaths(List<string> literals)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lit in literals.Take(20))
        {
            foreach (Match m in FilePathRegex.Matches(lit))
                if (m.Value.Length > 3) paths.Add(m.Value.Trim('"', '\''));
        }
        return paths.OrderBy(p => p, StringComparer.Ordinal).Take(5).ToList();
    }

    private static List<string> ExtractConfigKeys(List<string> literals)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lit in literals.Take(20))
        {
            foreach (Match m in ConfigKeyRegex.Matches(lit))
            {
                for (var i = 1; i < m.Groups.Count; i++)
                    if (m.Groups[i].Success && m.Groups[i].Value.Length > 1)
                        keys.Add(m.Groups[i].Value);
            }
        }
        return keys.OrderBy(k => k, StringComparer.Ordinal).Take(5).ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    // Reverse Index
    // ═══════════════════════════════════════════════════════════════

    private static void AddToReverseIndex(ReverseIndex index, MethodSemanticDoc doc, string projectName)
    {
        var entry = new ReverseIndexEntry
        {
            MethodId = doc.MethodId,
            MethodName = doc.MethodName,
            ClassName = doc.ClassName,
            ProjectName = projectName,
        };

        foreach (var table in doc.SqlTables)
            index.Add("table", table, entry);
        foreach (var url in doc.HttpUrls)
            index.Add("http", url, entry);
        foreach (var ex in doc.ExceptionTypes)
            index.Add("exception", ex, entry);
        foreach (var ck in doc.ConfigKeys)
            index.Add("config", ck, entry);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static string ShortenId(string id) =>
        id.Contains("::") ? id[(id.LastIndexOf("::") + 2)..] : id;

    private static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static bool IsKeyword(string word) =>
        word.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
        || word.Equals("INSERT", StringComparison.OrdinalIgnoreCase)
        || word.Equals("UPDATE", StringComparison.OrdinalIgnoreCase)
        || word.Equals("DELETE", StringComparison.OrdinalIgnoreCase)
        || word.Equals("FROM", StringComparison.OrdinalIgnoreCase)
        || word.Equals("WHERE", StringComparison.OrdinalIgnoreCase)
        || word.Equals("JOIN", StringComparison.OrdinalIgnoreCase)
        || word.Equals("SET", StringComparison.OrdinalIgnoreCase)
        || word.Equals("INTO", StringComparison.OrdinalIgnoreCase)
        || word.Equals("VALUES", StringComparison.OrdinalIgnoreCase)
        || word.Equals("AND", StringComparison.OrdinalIgnoreCase)
        || word.Equals("OR", StringComparison.OrdinalIgnoreCase)
        || word.Equals("NOT", StringComparison.OrdinalIgnoreCase)
        || word.Equals("NULL", StringComparison.OrdinalIgnoreCase)
        || word.Equals("LIKE", StringComparison.OrdinalIgnoreCase)
        || word.Equals("ORDER", StringComparison.OrdinalIgnoreCase)
        || word.Equals("GROUP", StringComparison.OrdinalIgnoreCase)
        || word.Equals("HAVING", StringComparison.OrdinalIgnoreCase);
}

public class DocBuilderOptions
{
    public int MaxStringLiterals { get; init; } = 100;
    public int MaxCalledMethods { get; init; } = 10;
    public int MaxCallers { get; init; } = 5;

    public static DocBuilderOptions Default => new();
}

public sealed class SemanticDocResult
{
    public List<MethodSemanticDoc> Docs { get; } = new();
    public ReverseIndex ReverseIndex { get; } = new();
    public int DocCount => Docs.Count;
}
