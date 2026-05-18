// =============================================================================
// GenericResolution/RepositoryPatternDetector.cs — Repository Pattern 检测器
// =============================================================================
// 识别 Repository / DAO / Service 模式的命名约定和泛型签名。
// 输出 PatternMatchResult 包含 entity type 和 pattern 类型。
// =============================================================================

using System.Text.RegularExpressions;

namespace Core.Graph.Analysis.GenericResolution;

public sealed class RepositoryPatternDetector
{
    // Repository 模式后缀
    private static readonly HashSet<string> RepositorySuffixes = new(StringComparer.Ordinal)
    {
        "Repository", "Repo", "Dao", "DAO", "Dal",
        "DataAccess", "DataProvider", "QueryProvider"
    };

    // Service 模式后缀（也持有 Entity 引用）
    private static readonly HashSet<string> ServiceSuffixes = new(StringComparer.Ordinal)
    {
        "Service", "ServiceImpl", "Manager", "Handler",
        "Provider", "Processor", "Controller"
    };

    // 泛型 Repository/DAO 基类名模式
    private static readonly HashSet<string> GenericRepositoryPatterns = new(StringComparer.Ordinal)
    {
        "Repository", "BaseRepository", "GenericRepository",
        "Dao", "BaseDao", "GenericDao",
        "BaseService", "GenericService",
        "CrudRepository", "PagingAndSortingRepository",
        "JpaRepository", "MongoRepository"
    };

    // IRepository 接口模式
    private static readonly HashSet<string> InterfaceRepositoryPatterns = new(StringComparer.Ordinal)
    {
        "IRepository", "IDao", "IReadRepository",
        "IWriteRepository", "ICrudService", "IEntityService"
    };

    public PatternMatchResult? Detect(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return null;

        // 去除命名空间前缀
        var simpleName = className.Contains('.')
            ? className[(className.LastIndexOf('.') + 1)..]
            : className;

        // 去除 "I" 前缀用于接口检测
        var withoutI = simpleName.StartsWith("I", StringComparison.Ordinal) && simpleName.Length > 1
            ? simpleName[1..]
            : simpleName;

        var result = new PatternMatchResult
        {
            ClassName = className,
            SimpleName = simpleName
        };

        // ① 检查是否是泛型 Repository 类型（如 Repository<T>）
        foreach (var pattern in GenericRepositoryPatterns)
        {
            if (string.Equals(simpleName, pattern, StringComparison.OrdinalIgnoreCase))
            {
                result.PatternType = "generic-repository";
                result.IsRepositoryPattern = true;
                return result;
            }

            if (string.Equals(withoutI, pattern, StringComparison.OrdinalIgnoreCase))
            {
                result.PatternType = "generic-repository-interface";
                result.IsRepositoryPattern = true;
                return result;
            }
        }

        // ② 检查 Interface Repository 模式
        foreach (var pattern in InterfaceRepositoryPatterns)
        {
            if (string.Equals(simpleName, pattern, StringComparison.OrdinalIgnoreCase))
            {
                result.PatternType = "interface-repository";
                result.IsRepositoryPattern = true;
                return result;
            }
        }

        // ③ 检查后缀
        foreach (var suffix in RepositorySuffixes)
        {
            if (simpleName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                result.PatternType = $"repository-suffix:{suffix}";
                result.IsRepositoryPattern = true;
                // 尝试从类名提取 entity name
                result.EntityType = ExtractEntityFromName(simpleName, suffix);
                return result;
            }
        }

        // ④ Service 后缀（也是一种 Repository 模式）
        foreach (var suffix in ServiceSuffixes)
        {
            if (simpleName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                result.PatternType = $"service-suffix:{suffix}";
                result.IsRepositoryPattern = true;
                result.EntityType = ExtractEntityFromName(simpleName, suffix);
                return result;
            }
        }

        return null;
    }

    public bool IsRepositoryType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        foreach (var pattern in GenericRepositoryPatterns)
            if (typeName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, pattern, StringComparison.OrdinalIgnoreCase))
                return true;

        foreach (var suffix in RepositorySuffixes)
            if (typeName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;

        foreach (var suffix in ServiceSuffixes)
            if (typeName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;

        foreach (var pattern in InterfaceRepositoryPatterns)
            if (typeName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static string? ExtractEntityFromName(string className, string suffix)
    {
        if (!className.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return null;

        var prefix = className[..^suffix.Length];
        if (prefix.Length == 0)
            return null;

        // ServiceImpl → 去掉 Impl
        if (prefix.EndsWith("Impl", StringComparison.OrdinalIgnoreCase))
            prefix = prefix[..^4];

        return prefix;
    }

    /// <summary>
    /// 从带泛型参数的类型名提取 Entity。
    /// 例如: "IRepository<EQA_Reagent>" → "EQA_Reagent"
    /// </summary>
    public static string? ExtractEntityFromGenericType(string genericTypeName)
    {
        var match = Regex.Match(genericTypeName, @"<([^>]+)>");
        if (!match.Success)
            return null;

        var typeArg = match.Groups[1].Value.Trim();

        // 排除非 entity 的泛型参数
        if (typeArg is "T" or "TEntity" or "TKey" or "TValue"
            || Regex.IsMatch(typeArg, @"^T\d+$"))
            return null;

        if (new[] { "int", "string", "long", "bool", "Guid", "DateTime", "object" }
            .Contains(typeArg, StringComparer.OrdinalIgnoreCase))
            return null;

        return typeArg;
    }
}

public sealed class PatternMatchResult
{
    public string ClassName { get; set; } = "";

    public string SimpleName { get; set; } = "";

    public string PatternType { get; set; } = "";

    public bool IsRepositoryPattern { get; set; }

    public string? EntityType { get; set; }
}
