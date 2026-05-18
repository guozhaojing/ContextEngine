// =============================================================================
// GenericResolution/GenericInvocationResolver.cs — 泛型方法调用解析器
// =============================================================================
// 从方法体内的调用表达式解析 Entity：
//   1. 检测 session.Query<T>() 等 NHibernate 泛型 API 调用
//   2. 检测 repo.Query(), dao.Get() 等 Repository 方法调用
//   3. 通过字段/变量类型追踪到具体的 Entity
// =============================================================================

using System.Text.RegularExpressions;

namespace Core.Graph.Analysis.GenericResolution;

public sealed class GenericInvocationResolver
{
    private readonly GenericInheritanceMap _inheritanceMap;
    private readonly GenericTypeResolver _typeResolver;
    private readonly RepositoryPatternDetector _patternDetector;

    // NHibernate Session API 方法名集合
    private static readonly HashSet<string> NhSessionMethods = new(StringComparer.Ordinal)
    {
        "Query", "Get", "Load", "Save", "Update", "Delete", "SaveOrUpdate",
        "CreateQuery", "CreateSQLQuery", "CreateCriteria",
        "QueryOver", "GetNamedQuery", "UniqueResult", "List", "Count"
    };

    // Repository/DAO 模式方法名集合（即使不是 NH Session 直接调用，也是数据访问）
    private static readonly HashSet<string> RepositoryMethods = new(StringComparer.Ordinal)
    {
        "GetById", "FindById", "GetAll", "FindAll", "GetList", "FindBy",
        "GetBy", "QueryBy", "Save", "Update", "Delete", "Insert",
        "GetList", "SearchBy", "CountBy", "ExistsBy",
        "GetEntity", "FindEntity", "QueryList", "QueryCount"
    };

    // 泛型 API 模式集合
    private static readonly HashSet<string> GenericApiMethods = new(StringComparer.Ordinal)
    {
        "Query", "Get", "Load", "Save", "Delete", "Update",
        "FindById", "GetById", "FindBy", "GetBy"
    };

    public GenericInvocationResolver(GenericInheritanceMap inheritanceMap)
    {
        _inheritanceMap = inheritanceMap;
        _typeResolver = new GenericTypeResolver(inheritanceMap);
        _patternDetector = new RepositoryPatternDetector();
    }

    /// <summary>
    /// 解析方法体中所有泛型/Repository 调用，返回 (methodId, entity, confidence) 列表。
    /// </summary>
    public List<ResolvedInvocation> ResolveInvocations(
        string sourceContent,
        string filePath,
        string methodClassName,
        string projectPath)
    {
        var results = new List<ResolvedInvocation>();

        // 提取所有方法调用表达式
        var invocations = ExtractInvocations(sourceContent);
        var variables = ExtractVariableTypes(sourceContent, methodClassName);

        foreach (var inv in invocations)
        {
            // ① 直接泛型调用: session.Query<EQA_Reagent>()
            if (!string.IsNullOrEmpty(inv.GenericArg))
            {
                if (_typeResolver.ResolveEntityFromInvocation(
                        inv.Receiver, inv.Method, inv.GenericArg, methodClassName)
                    is { Count: > 0 } entities)
                {
                    foreach (var entity in entities)
                    {
                        results.Add(ResolvedInvocation.Create(
                            inv.FullExpression, entity.EntityClass,
                            entity.Confidence, entity.ResolutionType,
                            filePath, methodClassName));
                    }
                }
            }

            // ② Session API 调用
            if (NhSessionMethods.Contains(inv.Method))
            {
                var receiverLower = inv.Receiver.ToLowerInvariant();
                var isSession = receiverLower.Contains("session")
                    || receiverLower.Contains("_session")
                    || receiverLower.EndsWith("session");

                if (isSession && !string.IsNullOrEmpty(inv.GenericArg))
                {
                    results.Add(ResolvedInvocation.Create(
                        inv.FullExpression, inv.GenericArg,
                        GenericResolutionConfidence.Exact, "session-generic",
                        filePath, methodClassName));
                }
            }

            // ③ Repository 方法调用: 通过 receiver 类型解析
            if (RepositoryMethods.Contains(inv.Method) || GenericApiMethods.Contains(inv.Method))
            {
                // 查找 receiver 变量的类型
                var receiverType = variables.GetValueOrDefault(inv.Receiver)
                    ?? ResolveReceiverType(inv.Receiver, sourceContent);

                if (!string.IsNullOrEmpty(receiverType))
                {
                    var entities = _typeResolver.ResolveEntityFromFieldType(receiverType, methodClassName);
                    foreach (var entity in entities.Take(3))
                    {
                        results.Add(ResolvedInvocation.Create(
                            inv.FullExpression, entity.EntityClass,
                            entity.Confidence, $"repo-receiver:{entity.ResolutionType}",
                            filePath, methodClassName));
                    }
                }
            }

            // ④ DAO/Repository 直接类型调用: GetListBySpecialtyID()
            var patternResult = _patternDetector.Detect(methodClassName);
            if (patternResult?.EntityType is not null
                && (RepositoryMethods.Contains(inv.Method) || GenericApiMethods.Contains(inv.Method)))
            {
                results.Add(ResolvedInvocation.Create(
                    inv.FullExpression, patternResult.EntityType,
                    GenericResolutionConfidence.High, "class-pattern",
                    filePath, methodClassName));
            }
        }

        return results.DistinctBy(r => $"{r.Expression}|{r.EntityClass}", StringComparer.Ordinal).ToList();
    }

    private List<InvocationInfo> ExtractInvocations(string content)
    {
        var results = new List<InvocationInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 匹配模式: receiver.Method<GenericArg>(...) 或 receiver.Method(...)
        var pattern = new Regex(
            @"(?:(?:(\w+(?:\.\w+)*)\s*\.\s*)|(?:\b))(\w+)\s*(?:<([^>]+)>)?\s*\(",
            RegexOptions.Compiled);

        foreach (Match match in pattern.Matches(content))
        {
            var receiver = match.Groups[1].Value;
            var method = match.Groups[2].Value;
            var genericArg = match.Groups[3].Value;

            if (string.IsNullOrEmpty(method))
                continue;

            var key = $"{receiver}.{method}<{genericArg}>";
            if (!seen.Add(key))
                continue;

            results.Add(new InvocationInfo
            {
                Receiver = receiver,
                Method = method,
                GenericArg = genericArg,
                FullExpression = key
            });
        }

        return results;
    }

    private Dictionary<string, string> ExtractVariableTypes(string content, string containingClass)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);

        // 字段声明: private IRepository<EQA_Reagent> _repo;
        var fieldPattern = new Regex(
            @"(?:private|protected|public|internal|readonly|static)\s+" +
            @"([\w.<>,\s]+?)\s+(_?\w+)\s*(?:=|;)",
            RegexOptions.Compiled);

        foreach (Match match in fieldPattern.Matches(content))
        {
            var typeName = match.Groups[1].Value.Trim();
            var varName = match.Groups[2].Value.Trim();
            if (!types.ContainsKey(varName))
                types[varName] = CleanTypeName(typeName);
        }

        // 局部变量声明: var repo = new Repository<EQA_Reagent>();
        var varPattern = new Regex(
            @"var\s+(\w+)\s*=\s*new\s+([\w.<>]+)\s*\(",
            RegexOptions.Compiled);

        foreach (Match match in varPattern.Matches(content))
        {
            var varName = match.Groups[1].Value;
            var typeName = match.Groups[2].Value.Trim();
            if (!types.ContainsKey(varName))
                types[varName] = CleanTypeName(typeName);
        }

        // 明确类型的局部变量: Repository<EQA_Reagent> repo;
        var typedVarPattern = new Regex(
            @"([\w.<>]+)\s+(_?\w+)\s*=\s*(?:new\s+\1|[^;]+|)",
            RegexOptions.Compiled);

        foreach (Match match in typedVarPattern.Matches(content))
        {
            var typeName = match.Groups[1].Value.Trim();
            var varName = match.Groups[2].Value.Trim();
            if (!types.ContainsKey(varName) && !IsKeyword(varName)
                && char.IsUpper(typeName[0]))
                types[varName] = CleanTypeName(typeName);
        }

        // 构造函数注入: this._repo = repo; → 查找参数类型
        var ctorParamPattern = new Regex(
            @"(\w+)\s+(\w+)\s*[,)]",
            RegexOptions.Compiled);

        // 简单处理：在构造函数中 this._field = param;
        var assignPattern = new Regex(
            @"(?:this\.|_)?(\w+)\s*=\s*(\w+)\s*;",
            RegexOptions.Compiled);

        // 方法参数: void Foo(IRepository<EQA_Reagent> repo)
        var paramPattern = new Regex(
            @"(\w+)\s+([\w.<>]+)\s+(\w+)\s*[,)]",
            RegexOptions.Compiled);

        foreach (Match match in paramPattern.Matches(content))
        {
            var typeName = match.Groups[1].Value.Trim();
            var varName = match.Groups[3].Value.Trim();
            if (!types.ContainsKey(varName) && !IsKeyword(varName)
                && char.IsUpper(typeName[0]))
                types[varName] = CleanTypeName(typeName);
        }

        return types;
    }

    private static string ResolveReceiverType(string receiverName, string content)
    {
        // 简单的变量追踪：在 content 中查找 receiverName 的声明
        var pattern = new Regex(
            $@"(?:Repository|IRepository|BaseDao|IDao|IService|BaseService)<\s*(\w+)\s*>\s+{Regex.Escape(receiverName)}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var match = pattern.Match(content);
        if (match.Success)
            return match.Value.Split('<', '>')[0].Trim() + "<" + match.Groups[1].Value + ">";

        return "";
    }

    private static string CleanTypeName(string typeName) =>
        typeName.Replace("?", "").Trim();

    private static bool IsKeyword(string name) =>
        name is "if" or "else" or "for" or "foreach" or "while" or "do"
            or "switch" or "case" or "return" or "new" or "var" or "null"
            or "true" or "false" or "this" or "base" or "in" or "out" or "ref";
}

public sealed class InvocationInfo
{
    public string Receiver { get; set; } = "";

    public string Method { get; set; } = "";

    public string GenericArg { get; set; } = "";

    public string FullExpression { get; set; } = "";
}

public sealed class ResolvedInvocation
{
    public string Expression { get; set; } = "";

    public string EntityClass { get; set; } = "";

    public GenericResolutionConfidence Confidence { get; set; }

    public string ResolutionMethod { get; set; } = "";

    public string? SourceFile { get; set; }

    public string? ContainingClass { get; set; }

    public static ResolvedInvocation Create(
        string expression,
        string entityClass,
        GenericResolutionConfidence confidence,
        string resolutionMethod,
        string? sourceFile = null,
        string? containingClass = null) => new()
        {
            Expression = expression,
            EntityClass = entityClass,
            Confidence = confidence,
            ResolutionMethod = resolutionMethod,
            SourceFile = sourceFile,
            ContainingClass = containingClass
        };
}
