// =============================================================================
// GenericResolution/DaoFieldDetector.cs — BLL 类中的 DAO 字段检测 (Roslyn SyntaxTree)
// =============================================================================
// 使用 Roslyn FieldDeclarationSyntax / PropertyDeclarationSyntax 替代 regex。
//
// 【Strict】只通过类型名检测 DAO 字段，禁止基于字段名推断：
//   ✔ private BModuleGridControlListDao _dao;     → 类型含 Dao 后缀
//   ✔ protected IDBaseDao<BModuleGridControlList, long> _baseDao; → 类型含 BaseDao
//   ✔ public IBBModuleGridControlList dao;         → 类型含 Dao 相关模式
//   ❌ private IGenericManager<T> _manager;        → 名称匹配 dao/repository（已禁止）
// =============================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Graph.Analysis.GenericResolution;

public sealed class DaoFieldDetector
{
    private readonly EntityClassRegistry _registry;
    private readonly GenericInheritanceMap _inheritanceMap;

    public DaoFieldDetector(EntityClassRegistry registry, GenericInheritanceMap inheritanceMap)
    {
        _registry = registry;
        _inheritanceMap = inheritanceMap;
    }

    public Dictionary<string, DaoFieldMatch> Detect(string fileContent, string filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();
        return DetectFromNode(root);
    }

    public Dictionary<string, DaoFieldMatch> DetectInClass(
        string fileContent,
        string filePath,
        string className)
    {
        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => string.Equals(
                GetFullClassName(c), className, StringComparison.Ordinal));

        if (classDecl is null)
            return new Dictionary<string, DaoFieldMatch>(StringComparer.Ordinal);

        return DetectFromClassDeclaration(classDecl);
    }

    public Dictionary<string, DaoFieldMatch> DetectFromClassDeclaration(ClassDeclarationSyntax classDecl)
    {
        var matches = new Dictionary<string, DaoFieldMatch>(StringComparer.Ordinal);

        foreach (var member in classDecl.Members)
        {
            if (member is FieldDeclarationSyntax fieldDecl)
            {
                var typeSyntax = fieldDecl.Declaration.Type;
                var typeName = typeSyntax.ToString();

                foreach (var variable in fieldDecl.Declaration.Variables)
                {
                    var match = TryMatchDaoField(typeName, variable.Identifier.Text);
                    if (match is not null && !matches.ContainsKey(match.FieldTypeName))
                        matches[match.FieldTypeName] = match;
                }
            }
            else if (member is PropertyDeclarationSyntax propDecl)
            {
                var typeName = propDecl.Type.ToString();
                var match = TryMatchDaoField(typeName, propDecl.Identifier.Text);
                if (match is not null && !matches.ContainsKey(match.FieldTypeName))
                    matches[match.FieldTypeName] = match;
            }
        }

        return matches;
    }

    public Dictionary<string, DaoFieldMatch> DetectFromNode(SyntaxNode root)
    {
        var matches = new Dictionary<string, DaoFieldMatch>(StringComparer.Ordinal);

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var classMatches = DetectFromClassDeclaration(classDecl);
            foreach (var (key, match) in classMatches)
            {
                if (!matches.ContainsKey(key))
                    matches[key] = match;
            }
        }

        return matches;
    }

    private DaoFieldMatch? TryMatchDaoField(string typeName, string fieldName)
    {
        var cleanedType = CleanTypeName(typeName);
        if (!IsDaoType(cleanedType))
            return null;

        var entityName = ResolveEntityFromType(cleanedType);
        var daoClassName = ExtractClassName(cleanedType);

        return new DaoFieldMatch
        {
            FieldName = fieldName,
            FieldTypeName = cleanedType,
            DaoClassName = daoClassName ?? "",
            EntityName = entityName,
            Confidence = entityName is not null
                ? GenericResolutionConfidence.High
                : GenericResolutionConfidence.Medium
        };
    }

    private static bool IsDaoType(string typeName)
    {
        if (typeName.EndsWith("Dao", StringComparison.OrdinalIgnoreCase) &&
            !typeName.EndsWith("IDao", StringComparison.OrdinalIgnoreCase))
            return true;

        if (typeName.Contains("BaseDao", StringComparison.OrdinalIgnoreCase))
            return true;

        if (typeName.Contains("IDBaseDao", StringComparison.Ordinal))
            return true;

        if (typeName.Contains("DaoNHB", StringComparison.OrdinalIgnoreCase))
            return true;

        if (typeName.Contains(".IDao", StringComparison.Ordinal))
            return true;

        if (NamePatterns.IsInterfaceDaoPattern(typeName))
            return true;

        return false;
    }

    private string? ResolveEntityFromType(string typeName)
    {
        var className = ExtractClassName(typeName);
        if (className is null)
            return null;

        var binding = _registry.GetBindingForClass(className);
        if (binding is not null)
            return binding.EntityType;

        var directEntity = ExtractGenericEntity(typeName);
        if (directEntity is not null && IsLikelyEntityType(directEntity))
            return directEntity;

        return null;
    }

    private static string? ExtractGenericEntity(string typeName)
    {
        var lt = typeName.IndexOf('<');
        if (lt < 0)
            return null;

        var rest = typeName[(lt + 1)..];
        var depth = 0;
        var end = -1;

        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i] == '<') depth++;
            else if (rest[i] == '>')
            {
                if (depth == 0) { end = i; break; }
                depth--;
            }
        }

        if (end < 0)
            return null;

        rest = rest[..end];
        var firstArg = rest.Split(',')[0].Trim();

        if (IsLikelyEntityType(firstArg))
            return firstArg;

        return null;
    }

    private static string? ExtractClassName(string typeName)
    {
        var stripped = typeName.Trim();
        if (stripped.StartsWith("I", StringComparison.Ordinal) &&
            stripped.Length > 1 && char.IsUpper(stripped[1]))
            stripped = stripped[1..];
        var lt = stripped.IndexOf('<');
        if (lt >= 0)
            stripped = stripped[..lt];
        stripped = stripped.TrimEnd('?');
        return stripped;
    }

    private static string CleanTypeName(string typeName)
    {
        return typeName.Replace("readonly ", "")
            .Replace("static ", "")
            .Trim();
    }

    private static string GetFullClassName(TypeDeclarationSyntax typeDecl)
    {
        var names = new List<string>();
        for (SyntaxNode? current = typeDecl;
            current is TypeDeclarationSyntax t;
            current = current.Parent)
            names.Insert(0, t.Identifier.Text);

        return string.Join(".", names);
    }

    private static bool IsLikelyEntityType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;
        if (typeName is "int" or "string" or "bool" or "long" or "double" or "float"
            or "object" or "void" or "decimal" or "DateTime" or "Guid" or "byte"
            or "T" or "TEntity" or "TKey" or "TValue" or "T1")
            return false;
        if (NamePatterns.IsGenericParameter(typeName))
            return false;
        if (typeName.EndsWith("Exception", StringComparison.Ordinal)
            || typeName.EndsWith("Attribute", StringComparison.Ordinal)
            || typeName.EndsWith("Service", StringComparison.Ordinal)
            || typeName.EndsWith("BLL", StringComparison.Ordinal)
            || typeName.EndsWith("Dao", StringComparison.Ordinal)
            || typeName.StartsWith("I", StringComparison.Ordinal))
            return false;
        return char.IsUpper(typeName[0]) && typeName.Length >= 3;
    }
}

public sealed class DaoFieldMatch
{
    public string FieldName { get; set; } = "";
    public string FieldTypeName { get; set; } = "";
    public string DaoClassName { get; set; } = "";
    public string? EntityName { get; set; }
    public GenericResolutionConfidence Confidence { get; set; }
}
