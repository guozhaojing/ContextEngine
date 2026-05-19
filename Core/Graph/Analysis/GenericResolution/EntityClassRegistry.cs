// =============================================================================
// GenericResolution/EntityClassRegistry.cs — Entity↔Class 双向映射
// =============================================================================
// 从 GenericInheritanceMap 提取所有 BLL/DAO 类与其泛型 Entity 的映射关系。
//
// 识别规则：
//   - class X : BaseBLL<T>        → T 是 Entity，X 是 BLL class
//   - class X : BaseDaoNHB<T, T1> → T 是 Entity，X 是 DAO class
//   - class X : HibernateDaoSupport → 不直接给 T，向上层找
//   - class X : IDBaseDao<T, T1>  → T 是 Entity
//   - class X : IBBEntityBLL      → 从命名推断（去掉 IBB、BLL 前缀）
//
// 输出：
//   ClassToEntity[className]  = entityClassName
//   EntityToBll[entityName]   = {bllClass1, bllClass2, ...}
//   EntityToDao[entityName]   = {daoClass1, daoClass2, ...}
// =============================================================================

namespace Core.Graph.Analysis.GenericResolution;

public sealed class EntityClassRegistry
{
    private readonly Dictionary<string, string> _classToEntity = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _entityToBll = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _entityToDao = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bllToDao = new(StringComparer.Ordinal);
    private readonly HashSet<string> _allEntities = new(StringComparer.Ordinal);

    private static readonly HashSet<string> BllBaseTypes = new(StringComparer.Ordinal)
    {
        "BaseBLL", "BaseManager", "BaseService", "BLLBase",
        "BLL", "BaseBll", "GenericManager"
    };

    private static readonly HashSet<string> DaoBaseTypes = new(StringComparer.Ordinal)
    {
        "BaseDaoNHB", "BaseDao", "BaseDAO", "GenericDao",
        "HibernateDaoSupport", "DaoBase", "RepositoryBase",
        "BaseRepository", "GenericRepository"
    };

    private static readonly HashSet<string> DaoInterfaceTypes = new(StringComparer.Ordinal)
    {
        "IDBaseDao", "IDao", "IDAO", "IBaseDao", "IRepository"
    };

    public IReadOnlyDictionary<string, string> ClassToEntity => _classToEntity;
    public IReadOnlyDictionary<string, List<string>> EntityToBll => _entityToBll;
    public IReadOnlyDictionary<string, List<string>> EntityToDao => _entityToDao;
    public IReadOnlyDictionary<string, string> BllToDao => _bllToDao;
    public IReadOnlySet<string> AllEntities => _allEntities;
    public int EntityCount => _allEntities.Count;
    public int BllCount => _entityToBll.Values.Sum(v => v.Count);
    public int DaoCount => _entityToDao.Values.Sum(v => v.Count);

    public void Build(GenericInheritanceMap inheritanceMap)
    {
        _classToEntity.Clear();
        _entityToBll.Clear();
        _entityToDao.Clear();
        _bllToDao.Clear();
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
                    var entityType = ExtractEntityFromArgs(baseType.TypeArguments);
                    if (entityType is not null)
                    {
                        RegisterBllEntity(shortName, fullName, entityType);
                    }
                }
                else if (IsDaoBaseType(baseName))
                {
                    var entityType = ExtractEntityFromArgs(baseType.TypeArguments);
                    if (entityType is not null)
                    {
                        RegisterDaoEntity(shortName, fullName, entityType);
                    }
                }
                else if (IsDaoInterface(baseName))
                {
                    var entityType = ExtractEntityFromArgs(baseType.TypeArguments);
                    if (entityType is not null)
                    {
                        RegisterDaoEntity(shortName, fullName, entityType);
                    }
                }
                else
                {
                    foreach (var typeArg in baseType.TypeArguments)
                    {
                        if (IsLikelyEntity(typeArg))
                        {
                            if (IsBllClass(shortName))
                                RegisterBllEntity(shortName, fullName, typeArg);
                            else if (IsDaoClass(shortName))
                                RegisterDaoEntity(shortName, fullName, typeArg);
                        }
                    }
                }
            }
        }

        LinkBllToDao();
    }

    public string? GetEntityForClass(string className)
    {
        _classToEntity.TryGetValue(className, out var entity);
        return entity;
    }

    public IReadOnlyList<string> GetBllClassesForEntity(string entityName)
    {
        _entityToBll.TryGetValue(entityName, out var list);
        return list ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public IReadOnlyList<string> GetDaoClassesForEntity(string entityName)
    {
        _entityToDao.TryGetValue(entityName, out var list);
        return list ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public bool IsBllClass(string className)
    {
        return _classToEntity.ContainsKey(className) &&
               _entityToBll.Values.Any(v => v.Contains(className));
    }

    public bool IsDaoClass(string className)
    {
        return _classToEntity.ContainsKey(className) &&
               _entityToDao.Values.Any(v => v.Contains(className));
    }

    private void RegisterBllEntity(string shortName, string fullName, string entityType)
    {
        _classToEntity[shortName] = entityType;
        _classToEntity[fullName] = entityType;
        _allEntities.Add(entityType);

        if (!_entityToBll.TryGetValue(entityType, out var bllList))
        {
            bllList = new List<string>();
            _entityToBll[entityType] = bllList;
        }
        if (!bllList.Contains(fullName))
            bllList.Add(fullName);
    }

    private void RegisterDaoEntity(string shortName, string fullName, string entityType)
    {
        _classToEntity[shortName] = entityType;
        _classToEntity[fullName] = entityType;
        _allEntities.Add(entityType);

        if (!_entityToDao.TryGetValue(entityType, out var daoList))
        {
            daoList = new List<string>();
            _entityToDao[entityType] = daoList;
        }
        if (!daoList.Contains(fullName))
            daoList.Add(fullName);
    }

    private void LinkBllToDao()
    {
        foreach (var (entity, bllClasses) in _entityToBll)
        {
            if (!_entityToDao.TryGetValue(entity, out var daoClasses))
                continue;

            foreach (var bll in bllClasses)
            {
                var bllShort = bll.Contains('.') ? bll[(bll.LastIndexOf('.') + 1)..] : bll;

                foreach (var dao in daoClasses)
                {
                    var daoShort = dao.Contains('.') ? dao[(dao.LastIndexOf('.') + 1)..] : dao;

                    if (daoShort.Contains(bllShort, StringComparison.OrdinalIgnoreCase) ||
                        bllShort.Contains(daoShort, StringComparison.OrdinalIgnoreCase))
                    {
                        _bllToDao[bllShort] = dao;
                        break;
                    }
                }

                if (!_bllToDao.ContainsKey(bllShort) && daoClasses.Count == 1)
                {
                    _bllToDao[bllShort] = daoClasses[0];
                }
            }
        }
    }

    private static string? ExtractEntityFromArgs(List<string> typeArgs)
    {
        if (typeArgs.Count == 0) return null;
        var first = typeArgs[0];
        if (IsLikelyEntity(first)) return first;

        return typeArgs.FirstOrDefault(IsLikelyEntity);
    }

    private static bool IsLikelyEntity(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        if (typeName is "int" or "string" or "bool" or "long" or "double" or "float"
            or "object" or "void" or "decimal" or "DateTime" or "Guid" or "byte"
            or "T" or "TEntity" or "TKey" or "TValue" or "T1") return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(typeName, @"^T\d*$")) return false;
        if (typeName.EndsWith("Exception", StringComparison.Ordinal)
            || typeName.EndsWith("Attribute", StringComparison.Ordinal)
            || typeName.EndsWith("EventArgs", StringComparison.Ordinal)
            || typeName.EndsWith("Delegate", StringComparison.Ordinal)
            || typeName.EndsWith("Handler", StringComparison.Ordinal)
            || typeName.EndsWith("Controller", StringComparison.Ordinal)
            || typeName.StartsWith("I", StringComparison.Ordinal)) return false;
        return char.IsUpper(typeName[0]) && typeName.Length >= 3;
    }

    private static bool IsBllBaseType(string baseName)
    {
        foreach (var pattern in BllBaseTypes)
        {
            if (baseName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
            if (baseName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsDaoBaseType(string baseName)
    {
        foreach (var pattern in DaoBaseTypes)
        {
            if (baseName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
            if (baseName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
            if (baseName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsDaoInterface(string baseName)
    {
        foreach (var pattern in DaoInterfaceTypes)
        {
            if (baseName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
            if (baseName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
