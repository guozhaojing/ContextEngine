// =============================================================================
// GenericResolution/EntityClassRegistry.cs — Entity↔Class 双向映射 (Strict Mode)
// =============================================================================
// 严格模式规则：
//   ✔ class X : BaseBLL<T>       → T 是 Entity（显式泛型绑定）
//   ✔ class X : BaseDaoNHB<T,T1> → T 是 Entity（显式泛型绑定）
//   ✔ class X : IDBaseDao<T,T1>  → T 是 Entity（显式泛型绑定）
//   ✔ class X : IBBModuleGridControlList → 从接口名推断（如 IBB + Entity名）
//   ❌ 禁止基于字段命名推断 Entity
//   ❌ 禁止 transitive closure 自动扩展
//
// 每个 Entity 携带 origin trace：SourceFile + BindingPath
// =============================================================================

namespace Core.Graph.Analysis.GenericResolution;

public sealed class EntityClassRegistry
{
    private readonly Dictionary<string, EntityBinding> _classToBinding = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<EntityBinding>> _entityToBll = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<EntityBinding>> _entityToDao = new(StringComparer.Ordinal);
    private readonly HashSet<string> _allEntities = new(StringComparer.Ordinal);

    private static readonly HashSet<string> BllBaseTypes = new(StringComparer.Ordinal)
    {
        "BaseBLL", "BaseManager", "BaseService", "BLLBase",
        "BLL", "BaseBll", "GenericManager"
    };

    private static readonly HashSet<string> DaoBaseTypes = new(StringComparer.Ordinal)
    {
        "BaseDaoNHB", "BaseDao", "BaseDAO", "GenericDao",
        "HibernateDaoSupport", "DaoBase"
    };

    private static readonly HashSet<string> DaoInterfaceTypes = new(StringComparer.Ordinal)
    {
        "IDBaseDao", "IBaseDao"
    };

    public IReadOnlyDictionary<string, EntityBinding> ClassToBinding => _classToBinding;
    public IReadOnlySet<string> AllEntities => _allEntities;
    public int EntityCount => _allEntities.Count;

    public void Build(GenericInheritanceMap inheritanceMap)
    {
        _classToBinding.Clear();
        _entityToBll.Clear();
        _entityToDao.Clear();
        _allEntities.Clear();

        foreach (var (fullName, classInfo) in inheritanceMap.Classes)
        {
            var shortName = classInfo.Name;

            foreach (var baseType in classInfo.BaseTypes)
            {
                if (!baseType.IsGeneric || baseType.TypeArguments.Count == 0)
                    continue;

                var baseName = baseType.Name;

                if (IsBllBaseType(baseName))
                {
                    var entityType = ExtractFirstConcreteArg(baseType.TypeArguments);
                    if (entityType is not null)
                    {
                        Register(shortName, fullName, entityType, EntityBindingKind.BllGeneric,
                            classInfo.SourceFile ?? "",
                            $"{shortName} : {baseType.FullName}");
                    }
                }
                else if (IsDaoBaseType(baseName))
                {
                    var entityType = ExtractFirstConcreteArg(baseType.TypeArguments);
                    if (entityType is not null)
                    {
                        Register(shortName, fullName, entityType, EntityBindingKind.DaoGeneric,
                            classInfo.SourceFile ?? "",
                            $"{shortName} : {baseType.FullName}");
                    }
                }
                else if (IsDaoInterface(baseName))
                {
                    var entityType = ExtractFirstConcreteArg(baseType.TypeArguments);
                    if (entityType is not null)
                    {
                        Register(shortName, fullName, entityType, EntityBindingKind.DaoInterface,
                            classInfo.SourceFile ?? "",
                            $"{shortName} : {baseType.FullName}");
                    }
                }
            }
        }
    }

    public EntityBinding? GetBindingForClass(string className)
    {
        _classToBinding.TryGetValue(className, out var binding);
        return binding;
    }

    public string? GetEntityForClass(string className)
    {
        return GetBindingForClass(className)?.EntityType;
    }

    public IReadOnlyList<EntityBinding> GetBllBindingsForEntity(string entityName)
    {
        _entityToBll.TryGetValue(entityName, out var list);
        return list ?? (IReadOnlyList<EntityBinding>)Array.Empty<EntityBinding>();
    }

    public IReadOnlyList<EntityBinding> GetDaoBindingsForEntity(string entityName)
    {
        _entityToDao.TryGetValue(entityName, out var list);
        return list ?? (IReadOnlyList<EntityBinding>)Array.Empty<EntityBinding>();
    }

    private void Register(
        string shortName,
        string fullName,
        string entityType,
        EntityBindingKind kind,
        string sourceFile,
        string bindingPath)
    {
        if (!IsValidEntityName(entityType))
            return;

        var binding = new EntityBinding
        {
            ClassName = fullName,
            ClassShortName = shortName,
            EntityType = entityType,
            Kind = kind,
            SourceFile = sourceFile,
            BindingPath = bindingPath
        };

        _classToBinding[fullName] = binding;
        _allEntities.Add(entityType);

        if (kind == EntityBindingKind.BllGeneric)
        {
            if (!_entityToBll.TryGetValue(entityType, out var list))
                _entityToBll[entityType] = list = new List<EntityBinding>();
            list.Add(binding);
        }
        else
        {
            if (!_entityToDao.TryGetValue(entityType, out var list))
                _entityToDao[entityType] = list = new List<EntityBinding>();
            list.Add(binding);
        }
    }

    private static string? ExtractFirstConcreteArg(List<string> typeArgs)
    {
        if (typeArgs.Count == 0) return null;
        var first = typeArgs[0];
        if (IsConcreteType(first)) return first;

        for (var i = 1; i < typeArgs.Count; i++)
            if (IsConcreteType(typeArgs[i]))
                return typeArgs[i];

        return null;
    }

    private static bool IsConcreteType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        if (typeName.Length <= 1) return false;
        if (char.IsLower(typeName[0])) return false;
        if (typeName.StartsWith("T", StringComparison.Ordinal) && typeName.Length <= 2) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(typeName, @"^T\d+$")) return false;

        return true;
    }

    private static bool IsValidEntityName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        if (typeName is "int" or "string" or "bool" or "long" or "double" or "float"
            or "object" or "void" or "decimal" or "DateTime" or "Guid" or "byte"
            or "T" or "TEntity" or "TKey" or "TValue" or "T1") return false;
        if (typeName.EndsWith("Exception", StringComparison.Ordinal)) return false;
        if (typeName.EndsWith("Attribute", StringComparison.Ordinal)) return false;
        if (typeName.EndsWith("EventArgs", StringComparison.Ordinal)) return false;
        if (typeName.EndsWith("Delegate", StringComparison.Ordinal)) return false;
        if (typeName.EndsWith("Handler", StringComparison.Ordinal)) return false;
        if (typeName.EndsWith("Controller", StringComparison.Ordinal)) return false;
        if (typeName.EndsWith("Service", StringComparison.Ordinal)) return false;
        if (typeName.EndsWith("BLL", StringComparison.Ordinal)) return false;
        if (typeName.EndsWith("DAO", StringComparison.Ordinal)) return false;
        if (typeName.EndsWith("Dao", StringComparison.Ordinal)) return false;
        if (typeName.StartsWith("I", StringComparison.Ordinal)) return false;
        if (typeName.Contains("VO", StringComparison.Ordinal)) return false;
        if (typeName.Contains("Dto", StringComparison.Ordinal)) return false;
        if (typeName.EndsWith("Entity", StringComparison.Ordinal) && typeName != "BaseEntity") return false;
        return char.IsUpper(typeName[0]) && typeName.Length >= 3;
    }

    private static bool IsBllBaseType(string baseName)
    {
        foreach (var pattern in BllBaseTypes)
            if (baseName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsDaoBaseType(string baseName)
    {
        foreach (var pattern in DaoBaseTypes)
            if (baseName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsDaoInterface(string baseName)
    {
        foreach (var pattern in DaoInterfaceTypes)
            if (baseName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

public sealed class EntityBinding
{
    public string ClassName { get; set; } = "";
    public string ClassShortName { get; set; } = "";
    public string EntityType { get; set; } = "";
    public EntityBindingKind Kind { get; set; }
    public string SourceFile { get; set; } = "";
    public string BindingPath { get; set; } = "";
}

public enum EntityBindingKind
{
    BllGeneric,
    DaoGeneric,
    DaoInterface
}
