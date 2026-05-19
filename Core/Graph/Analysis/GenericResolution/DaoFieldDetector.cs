// =============================================================================
// GenericResolution/DaoFieldDetector.cs — BLL 类中的 DAO 字段检测
// =============================================================================
// 检测 BLL 具体类（非 BaseBLL）中持有的 DAO 类型字段/属性：
//   public IDBModuleGridControlListDao dao;
//   private BModuleGridControlListDao _moduleGridDao;
//   protected IDBaseDao<BModuleGridControlList, long> _baseDao;
//
// 从 DAO 类型名反推该 BLL 类操作哪个 Entity。
// 产出：(bllClassName, daoFieldTypeName) → entityName 映射。
// =============================================================================

using System.Text.RegularExpressions;

namespace Core.Graph.Analysis.GenericResolution;

public sealed class DaoFieldDetector
{
    private readonly EntityClassRegistry _registry;
    private readonly GenericInheritanceMap _inheritanceMap;

    private static readonly HashSet<string> DaoFieldNamePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "dao", "repository", "repo", "_dao", "_repository", "_repo",
        "m_dao", "m_repository", "m_repo", "dataAccess", "dal", "IDao"
    };

    public DaoFieldDetector(EntityClassRegistry registry, GenericInheritanceMap inheritanceMap)
    {
        _registry = registry;
        _inheritanceMap = inheritanceMap;
    }

    public Dictionary<string, DaoFieldMatch> Detect(string fileContent, string filePath)
    {
        var matches = new Dictionary<string, DaoFieldMatch>(StringComparer.Ordinal);

        var lines = fileContent.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal))
                continue;

            var match = TryMatchDaoField(line);
            if (match is null) continue;

            if (!matches.ContainsKey(match.FieldTypeName))
                matches[match.FieldTypeName] = match;
        }

        return matches;
    }

    public Dictionary<string, DaoFieldMatch> DetectInClass(
        string fileContent,
        string filePath,
        string className)
    {
        var matches = new Dictionary<string, DaoFieldMatch>(StringComparer.Ordinal);

        var classBody = ExtractClassBody(fileContent, className);
        if (classBody is null) return matches;

        var lines = classBody.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;

            var match = TryMatchDaoField(line);
            if (match is null) continue;

            if (!matches.ContainsKey(match.FieldTypeName))
                matches[match.FieldTypeName] = match;
        }

        return matches;
    }

    private DaoFieldMatch? TryMatchDaoField(string line)
    {
        var fieldMatch = Regex.Match(line,
            @"(?:public|private|protected|internal|static|readonly)\s+" +
            @"([\w.<>, \t]+?)\s+" +
            @"(\w+)\s*[;=]");

        if (!fieldMatch.Success) return null;

        var typeName = CleanTypeName(fieldMatch.Groups[1].Value.Trim());
        var fieldName = fieldMatch.Groups[2].Value.Trim();

        if (!IsDaoField(fieldName, typeName)) return null;

        var entityName = ResolveEntityFromType(typeName);
        var daoClassName = ExtractClassName(typeName);

        return new DaoFieldMatch
        {
            FieldName = fieldName,
            FieldTypeName = typeName,
            DaoClassName = daoClassName ?? "",
            EntityName = entityName,
            Confidence = entityName is not null
                ? GenericResolutionConfidence.High
                : GenericResolutionConfidence.Medium
        };
    }

    private bool IsDaoField(string fieldName, string typeName)
    {
        foreach (var pattern in DaoFieldNamePatterns)
        {
            if (fieldName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (typeName.Contains("Dao", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("DAO", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("IDBaseDao", StringComparison.Ordinal) ||
            typeName.Contains("IDao", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private string? ResolveEntityFromType(string typeName)
    {
        var className = ExtractClassName(typeName);
        if (className is null) return null;

        var entity = _registry.GetEntityForClass(className);
        if (entity is not null) return entity;

        var stripped = StripInterfacePrefix(className);

        var entity2 = _registry.GetEntityForClass(stripped);
        if (entity2 is not null) return entity2;

        var classInfo = _inheritanceMap.FindClass(stripped);
        if (classInfo is not null)
        {
            var resolved = _inheritanceMap.ResolveConcreteType(classInfo, "T");
            if (resolved is not null) return resolved;
        }

        var genericEntity = ExtractGenericEntity(typeName);
        if (genericEntity is not null) return genericEntity;

        return null;
    }

    private static string? ExtractGenericEntity(string typeName)
    {
        var match = Regex.Match(typeName, @"<([\w.]+)[,\s>]");
        if (match.Success)
        {
            var arg = match.Groups[1].Value;
            if (IsLikelyEntityType(arg)) return arg;
        }
        return null;
    }

    private static string? ExtractClassName(string typeName)
    {
        var stripped = typeName.Trim();

        if (stripped.StartsWith("I", StringComparison.Ordinal) &&
            stripped.Length > 1 &&
            char.IsUpper(stripped[1]))
        {
            stripped = stripped[1..];
        }

        var lt = stripped.IndexOf('<');
        if (lt >= 0) stripped = stripped[..lt];

        stripped = stripped.TrimEnd('?');

        return stripped;
    }

    private static string StripInterfacePrefix(string typeName)
    {
        if (typeName.StartsWith("I", StringComparison.Ordinal) &&
            typeName.Length > 1 &&
            char.IsUpper(typeName[1]))
            return typeName[1..];
        return typeName;
    }

    private static string CleanTypeName(string typeName)
    {
        return typeName
            .Replace("readonly ", "")
            .Replace("static ", "")
            .Trim();
    }

    private static string? ExtractClassBody(string fileContent, string className)
    {
        var idx = fileContent.IndexOf("class " + className, StringComparison.Ordinal);
        if (idx < 0)
        {
            var shortName = className.Contains('.')
                ? className[(className.LastIndexOf('.') + 1)..]
                : className;
            idx = fileContent.IndexOf("class " + shortName, StringComparison.Ordinal);
        }
        if (idx < 0) return null;

        var depth = 0;
        var started = false;
        var startIdx = -1;

        for (var i = idx; i < fileContent.Length; i++)
        {
            var c = fileContent[i];
            if (c == '{') { depth++; started = true; if (startIdx < 0) startIdx = i + 1; }
            else if (c == '}') { depth--; if (depth == 0 && started) return fileContent[startIdx..i]; }
        }

        return null;
    }

    private static bool IsLikelyEntityType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        if (typeName is "int" or "string" or "bool" or "long" or "double" or "float"
            or "object" or "void" or "decimal" or "DateTime" or "Guid" or "byte"
            or "T" or "TEntity" or "TKey" or "TValue" or "T1") return false;
        if (Regex.IsMatch(typeName, @"^T\d*$")) return false;
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
