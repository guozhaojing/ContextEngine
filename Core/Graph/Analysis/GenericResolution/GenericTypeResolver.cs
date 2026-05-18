// =============================================================================
// GenericResolution/GenericTypeResolver.cs — 泛型类型参数解析器
// =============================================================================
// 核心职责：
//   1. 从 class 声明解析泛型类型参数替换
//      class ReagentRepo : BaseRepository<EQA_Reagent> → T = EQA_Reagent
//   2. 追踪多层继承链中的类型参数传递
//   3. 解析 IRepository<T> → 实现类 → entity type
//   4. 解析 typeof(T) / GetType() 反射模式
// =============================================================================

using System.Text.RegularExpressions;

namespace Core.Graph.Analysis.GenericResolution;

public sealed class GenericTypeResolver
{
    private readonly GenericInheritanceMap _inheritanceMap;
    private readonly RepositoryPatternDetector _patternDetector;

    public GenericTypeResolver(GenericInheritanceMap inheritanceMap)
    {
        _inheritanceMap = inheritanceMap ?? throw new ArgumentNullException(nameof(inheritanceMap));
        _patternDetector = new RepositoryPatternDetector();
    }

    /// <summary>
    /// 解析一个 class 实现的泛型接口/基类中 T 对应的具体实体类型。
    /// </summary>
    public List<EntityResolution> ResolveEntityFromClass(string className, string? ns = null)
    {
        var results = new List<EntityResolution>();
        var classInfo = _inheritanceMap.FindClass(className, ns);
        if (classInfo is null)
            return results;

        // ① 从自身泛型参数绑定
        foreach (var (param, bindings) in classInfo.GenericParameterBindings)
        {
            foreach (var binding in bindings)
            {
                if (IsEntityType(binding))
                {
                    results.Add(new EntityResolution
                    {
                        EntityClass = binding,
                        ResolutionType = "generic-binding",
                        Confidence = GenericResolutionConfidence.Exact,
                        ViaClass = classInfo.FullName
                    });
                }
            }
        }

        // ② 从 base type 泛型参数
        var resolvedArgs = _inheritanceMap.ResolveAllArguments(classInfo);
        foreach (var arg in resolvedArgs)
        {
            if (IsEntityType(arg.ConcreteType))
            {
                results.Add(new EntityResolution
                {
                    EntityClass = arg.ConcreteType,
                    ResolutionType = $"base-type:{arg.SourceBaseType}",
                    Confidence = arg.Confidence,
                    ViaClass = classInfo.FullName
                });
            }
        }

        // ③ 从 base type 的直接类型参数
        foreach (var baseType in classInfo.BaseTypes)
        {
            if (!baseType.IsGeneric)
                continue;

            foreach (var typeArg in baseType.TypeArguments)
            {
                if (IsEntityType(typeArg))
                {
                    var isRepositoryType = _patternDetector.IsRepositoryType(baseType.Name);
                    results.Add(new EntityResolution
                    {
                        EntityClass = typeArg,
                        ResolutionType = isRepositoryType
                            ? $"repository-base:{baseType.Name}"
                            : $"base-type-arg:{baseType.Name}",
                        Confidence = GenericResolutionConfidence.Exact,
                        ViaClass = classInfo.FullName
                    });
                }
            }
        }

        return results.DistinctBy(r => r.EntityClass, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 从方法名和所属 class 解析方法操作哪个 Entity。
    /// 例如：class ReagentRepo 中调用 GetList() → entity = EQA_Reagent
    /// </summary>
    public List<EntityResolution> ResolveEntityFromMethod(
        string className,
        string methodName,
        string? ns = null)
    {
        var results = new List<EntityResolution>();

        // 先从 class 解析
        var classEntities = ResolveEntityFromClass(className, ns);
        results.AddRange(classEntities);

        // 如果方法名包含 entity 线索
        var methodLower = methodName.ToLowerInvariant();
        foreach (var entity in _inheritanceMap.Classes.Values)
        {
            if (entity.TypeParameters.Count == 0)
                continue;

            var entityName = entity.Name;
            if (methodLower.Contains(entityName.ToLowerInvariant())
                || entityName.ToLowerInvariant().Contains(methodLower))
            {
                results.Add(new EntityResolution
                {
                    EntityClass = entityName,
                    ResolutionType = "method-name-hint",
                    Confidence = GenericResolutionConfidence.Low,
                    ViaClass = className
                });
            }
        }

        return results;
    }

    /// <summary>
    /// 从 field/property 的类型名解析 Entity。
    /// </summary>
    public List<EntityResolution> ResolveEntityFromFieldType(string typeName, string containingClass)
    {
        var results = new List<EntityResolution>();

        // 直接的类型名可能是 entity 类
        if (IsEntityType(typeName))
        {
            results.Add(new EntityResolution
            {
                EntityClass = typeName,
                ResolutionType = "field-type-direct",
                Confidence = GenericResolutionConfidence.High,
                ViaClass = containingClass
            });
        }

        // 检查是否是已知的 repository 类型 → 解析其泛型参数
        var patternResult = _patternDetector.Detect(typeName);
        if (patternResult is not null && patternResult.EntityType is not null)
        {
            results.Add(new EntityResolution
            {
                EntityClass = patternResult.EntityType,
                ResolutionType = "field-repository-pattern",
                Confidence = GenericResolutionConfidence.High,
                ViaClass = containingClass
            });
        }

        // 从 class 继承信息解析
        var classEntities = ResolveEntityFromClass(typeName);
        results.AddRange(classEntities);

        return results.DistinctBy(r => r.EntityClass, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 解析泛型方法调用的 Entity 类型。
    /// 从 invocation 上下文的泛型参数或 receiver 类型推导。
    /// </summary>
    public List<EntityResolution> ResolveEntityFromInvocation(
        string receiverType,
        string methodName,
        string? genericArgument,
        string containingClass)
    {
        var results = new List<EntityResolution>();

        // ① 显式泛型参数（最高置信度）
        if (!string.IsNullOrEmpty(genericArgument) && IsEntityType(genericArgument))
        {
            results.Add(new EntityResolution
            {
                EntityClass = genericArgument,
                ResolutionType = "generic-argument",
                Confidence = GenericResolutionConfidence.Exact,
                ViaClass = containingClass
            });
        }

        // ② 从 receiver 类型推导
        if (!string.IsNullOrEmpty(receiverType))
        {
            var fieldEntities = ResolveEntityFromFieldType(receiverType, containingClass);
            results.AddRange(fieldEntities.Select(e =>
            {
                e.ResolutionType = $"invocation-receiver:{e.ResolutionType}";
                return e;
            }));
        }

        // ③ 从方法名推断（如 GetReagent, SaveEquip）
        var methodLower = methodName.ToLowerInvariant();
        var entityPatterns = new[] { "get", "save", "delete", "update", "query", "load", "find", "create" };
        foreach (var prefix in entityPatterns)
        {
            if (methodLower.StartsWith(prefix, StringComparison.Ordinal))
            {
                var suffix = methodLower[prefix.Length..];
                if (suffix.Length >= 3)
                {
                    foreach (var cls in _inheritanceMap.Classes.Values)
                    {
                        if (cls.Name.Length >= 3 &&
                            suffix.Contains(cls.Name.ToLowerInvariant(), StringComparison.Ordinal))
                        {
                            results.Add(new EntityResolution
                            {
                                EntityClass = cls.Name,
                                ResolutionType = "method-name-pattern",
                                Confidence = GenericResolutionConfidence.Low,
                                ViaClass = containingClass
                            });
                        }
                    }
                }
            }
        }

        return results.DistinctBy(r => r.EntityClass, StringComparer.Ordinal).ToList();
    }

    private static bool IsEntityType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        // 排除常见非 entity 类型
        if (typeName is "int" or "string" or "bool" or "long" or "double" or "float"
            or "object" or "void" or "decimal" or "DateTime" or "Guid" or "byte"
            or "T" or "TEntity" or "TKey" or "TValue")
            return false;

        if (Regex.IsMatch(typeName, @"^T\d*$"))
            return false;

        if (typeName.EndsWith("Exception", StringComparison.Ordinal)
            || typeName.EndsWith("Attribute", StringComparison.Ordinal)
            || typeName.EndsWith("EventArgs", StringComparison.Ordinal)
            || typeName.EndsWith("Delegate", StringComparison.Ordinal))
            return false;

        return char.IsUpper(typeName[0]);
    }
}

public sealed class EntityResolution
{
    public string EntityClass { get; set; } = "";

    public string ResolutionType { get; set; } = "";

    public GenericResolutionConfidence Confidence { get; set; }

    public string ViaClass { get; set; } = "";
}
