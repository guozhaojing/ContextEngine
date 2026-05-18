using System.Xml.Linq;
using Core.Graph.Identity;
using Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Graph.Analysis.Analyzers;

public sealed class SpringBeanAnalyzer : IGraphAnalyzer
{
    public string Name => "spring-bean";

    private const string EdgeKindImplements = "spring:implements";
    private const string EdgeKindPropertyRef = "spring:property-ref";

    private static readonly HashSet<string> ExcludedDirNames =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules" };

    public void Analyze(GraphAnalysisContext context)
    {
        var scanRoot = context.Scan.ScanRoot;
        var allUnits = context.Scan.AllCodeUnits;

        // ① 发现并解析所有 Spring 配置文件
        var beanDefs = DiscoverBeanDefinitions(scanRoot);
        if (beanDefs.Count == 0)
            return;

        // ② 建立全量 CodeUnit 索引 (ClassName+Namespace → units)
        var unitsByType = BuildTypeUnitIndex(allUnits);

        // ③ 为每个 Bean 解析 interface → impl 映射
        var interfaceImplMap = ResolveInterfaceImplMap(beanDefs, unitsByType, scanRoot);

        // ④ 建立 interface method → impl method 映射 (双向)
        var methodMap = BuildMethodMap(interfaceImplMap, allUnits);

        // ⑤ 产出：Facts + Annotations + ExtraEdges
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);

        foreach (var bean in beanDefs)
        {
            // 找到该 Bean 的任意一个方法作为 subject anchor
            var beanMethodId = FindBeanAnchorMethod(bean, allUnits);
            if (beanMethodId is null)
                continue;

            var relativePath = NormalizeFilePath(bean.ConfigFilePath, scanRoot);

            // Fact: Bean 定义
            var propRefSummary = string.Join(", ",
                bean.PropertyRefs.Select(p => $"{p.Name}→{p.RefBeanId}"));
            var data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["beanId"] = bean.Id,
                ["beanType"] = bean.TypeFullName,
                ["namespace"] = bean.Namespace,
                ["className"] = bean.ClassName,
                ["framework"] = "spring.net"
            };
            if (propRefSummary.Length > 0)
                data["propertyRefs"] = propRefSummary;

            context.AddFact(beanMethodId, "spring-bean", GraphSubjectKinds.Method, relativePath, data);

            context.AddAnnotation(beanMethodId, "spring-bean-id", bean.Id, relativePath);
            context.AddAnnotation(beanMethodId, "spring-bean-type", bean.TypeFullName, relativePath);

            // ExtraEdge: interface → impl
            var implMappings = interfaceImplMap
                .Where(m => m.ImplClassName == bean.ClassName && m.ImplNamespace == bean.Namespace);

            foreach (var mapping in implMappings)
            {
                var ifaceUnits = FindTypeUnits(mapping.IfaceClassName, mapping.IfaceNamespace, unitsByType);
                var implUnits = FindTypeUnits(bean.ClassName, bean.Namespace, unitsByType);

                foreach (var ifaceUnit in ifaceUnits)
                {
                    var implUnit = FindMatchingMethod(ifaceUnit, implUnits);
                    if (implUnit is null)
                        continue;

                    var edgeKey = $"{ifaceUnit.Id}→{implUnit.Id}:{EdgeKindImplements}";
                    if (!seenEdges.Add(edgeKey))
                        continue;

                    context.AddExtraEdge(
                        fromId: ifaceUnit.Id,
                        toId: implUnit.Id,
                        kind: EdgeKindImplements,
                        label: $"{ifaceUnit.ClassName}.{ifaceUnit.MethodName}⇒{implUnit.ClassName}.{implUnit.MethodName}",
                        isResolved: true,
                        sourceFile: relativePath,
                        attributes: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["beanId"] = bean.Id
                        });
                }
            }

            // ExtraEdge: property ref → 依赖 Bean 的 Anchor Method
            foreach (var propRef in bean.PropertyRefs)
            {
                var targetBean = beanDefs.FirstOrDefault(b =>
                    string.Equals(b.Id, propRef.RefBeanId, StringComparison.Ordinal));
                if (targetBean is null)
                    continue;

                var targetAnchorId = FindBeanAnchorMethod(targetBean, allUnits);
                if (targetAnchorId is null)
                    continue;

                var refEdgeKey = $"{beanMethodId}→{targetAnchorId}:{EdgeKindPropertyRef}";
                if (!seenEdges.Add(refEdgeKey))
                    continue;

                context.AddExtraEdge(
                    fromId: beanMethodId,
                    toId: targetAnchorId,
                    kind: EdgeKindPropertyRef,
                    label: $"spring:ref:{propRef.RefBeanId}",
                    isResolved: true,
                    sourceFile: relativePath,
                    attributes: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["beanId"] = bean.Id,
                        ["refBeanId"] = propRef.RefBeanId,
                        ["propertyName"] = propRef.Name
                    });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Bean Definition Discovery
    // ═══════════════════════════════════════════════════════════════

    private static List<BeanDef> DiscoverBeanDefinitions(string scanRoot)
    {
        var beans = new List<BeanDef>();

        var xmlFiles = Directory.EnumerateFiles(scanRoot, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext == ".xml" || ext == ".config";
            })
            .Where(f => !IsUnderExcludedDirectory(f, scanRoot));

        foreach (var filePath in xmlFiles)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                ParseBeansFromDocument(doc, filePath, beans);
            }
            catch
            {
                // 非 XML 或 畸文跳过
            }
        }

        return beans;
    }

    private static void ParseBeansFromDocument(XDocument doc, string filePath, List<BeanDef> beans)
    {
        // 支持的根路径: <objects> (直接 Spring.NET)
        //               <spring><objects> (嵌入主机配置)
        //               <spring><context><components> (其他)
        var root = doc.Root;
        if (root is null)
            return;

        // 直接 <objects>
        if (string.Equals(root.Name.LocalName, "objects", StringComparison.OrdinalIgnoreCase))
        {
            ExtractBeanDefinitions(root, filePath, beans);
            return;
        }

        // 递归查找 <objects> 在后代中
        var objectsElement = root.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "objects", StringComparison.OrdinalIgnoreCase));

        if (objectsElement is not null)
            ExtractBeanDefinitions(objectsElement, filePath, beans);
    }

    private static void ExtractBeanDefinitions(XElement objectsElement, string filePath, List<BeanDef> beans)
    {
        foreach (var objElement in objectsElement.Elements())
        {
            if (!string.Equals(objElement.Name.LocalName, "object", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = objElement.Attribute("id")?.Value;
            var type = objElement.Attribute("type")?.Value;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type))
                continue;

            var (ns, className) = ParseTypeName(type);
            if (string.IsNullOrEmpty(className))
                continue;

            var propRefs = new List<PropRef>();
            foreach (var propElement in objElement.Elements())
            {
                if (!string.Equals(propElement.Name.LocalName, "property", StringComparison.OrdinalIgnoreCase))
                    continue;

                var propName = propElement.Attribute("name")?.Value;
                var propRef = propElement.Attribute("ref")?.Value;
                if (!string.IsNullOrWhiteSpace(propName) && !string.IsNullOrWhiteSpace(propRef))
                {
                    propRefs.Add(new PropRef(propName, propRef));
                }
            }

            beans.Add(new BeanDef(id, type, ns, className, propRefs, filePath));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Type Matching
    // ═══════════════════════════════════════════════════════════════

    private static Dictionary<string, List<CodeUnit>> BuildTypeUnitIndex(IReadOnlyList<CodeUnit> units)
    {
        var index = new Dictionary<string, List<CodeUnit>>(StringComparer.Ordinal);

        foreach (var unit in units)
        {
            var key = $"{unit.Namespace}|{unit.ClassName}".ToLowerInvariant();
            if (!index.TryGetValue(key, out var list))
            {
                list = new List<CodeUnit>();
                index[key] = list;
            }

            list.Add(unit);
        }

        return index;
    }

    private static List<CodeUnit> FindTypeUnits(
        string className,
        string namespaceName,
        Dictionary<string, List<CodeUnit>> index)
    {
        var key = $"{namespaceName}|{className}".ToLowerInvariant();
        return index.TryGetValue(key, out var list) ? list : new List<CodeUnit>();
    }

    private static CodeUnit? FindBeansFirstUnit(
        BeanDef bean,
        Dictionary<string, List<CodeUnit>> index)
    {
        var key = $"{bean.Namespace}|{bean.ClassName}".ToLowerInvariant();
        return index.TryGetValue(key, out var list) && list.Count > 0 ? list[0] : null;
    }

    // ═══════════════════════════════════════════════════════════════
    // Interface → Impl Mapping
    // ═══════════════════════════════════════════════════════════════

    private static List<InterfaceImplMapping> ResolveInterfaceImplMap(
        List<BeanDef> beanDefs,
        Dictionary<string, List<CodeUnit>> unitsByType,
        string scanRoot)
    {
        var mappings = new List<InterfaceImplMapping>();
        var sourceCache = new Dictionary<string, SyntaxTree?>(StringComparer.OrdinalIgnoreCase);

        foreach (var bean in beanDefs)
        {
            var firstUnit = FindBeansFirstUnit(bean, unitsByType);
            if (firstUnit is null)
                continue;

            var filePath = firstUnit.FilePath;
            if (!sourceCache.TryGetValue(filePath, out var tree))
            {
                tree = ParseSourceFile(filePath);
                sourceCache[filePath] = tree;
            }

            if (tree is null)
                continue;

            var root = tree.GetRoot();
            var classDecl = FindClass(root, bean.ClassName);

            if (classDecl?.BaseList is null)
                continue;

            foreach (var baseType in classDecl.BaseList.Types)
            {
                var ifaceName = GetSimpleTypeName(baseType.Type);
                if (string.IsNullOrEmpty(ifaceName) || !ifaceName.StartsWith("I", StringComparison.Ordinal))
                    continue;

                var ifaceNamespace = ResolveInterfaceNamespace(baseType.Type, root, bean.Namespace);

                mappings.Add(new InterfaceImplMapping(
                    ifaceName,
                    ifaceNamespace,
                    bean.ClassName,
                    bean.Namespace));
            }
        }

        return mappings;
    }

    private static string ResolveInterfaceNamespace(
        TypeSyntax typeSyntax,
        SyntaxNode root,
        string fallbackNamespace)
    {
        if (typeSyntax is QualifiedNameSyntax qualified)
        {
            return qualified.Left.ToString();
        }

        // 简单名 → 尝试通过 using 解析
        var nameToCheck = GetSimpleTypeName(typeSyntax);
        if (nameToCheck.Length == 0)
            return fallbackNamespace;

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (usingDirective.Name is QualifiedNameSyntax qns
                && string.Equals(qns.Right.Identifier.Text, nameToCheck, StringComparison.Ordinal))
            {
                return qns.Left.ToString();
            }
        }

        return fallbackNamespace;
    }

    // ═══════════════════════════════════════════════════════════════
    // Method Map (interfaceMethod → implMethod)
    // ═══════════════════════════════════════════════════════════════

    private static Dictionary<string, string> BuildMethodMap(
        List<InterfaceImplMapping> mappings,
        IReadOnlyList<CodeUnit> allUnits)
    {
        var methodMap = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var mapping in mappings)
        {
            var ifaceUnits = allUnits
                .Where(u => u.ClassName == mapping.IfaceClassName
                            && string.Equals(u.Namespace, mapping.IfaceNamespace, StringComparison.Ordinal))
                .ToList();

            var implUnits = allUnits
                .Where(u => u.ClassName == mapping.ImplClassName
                            && string.Equals(u.Namespace, mapping.ImplNamespace, StringComparison.Ordinal))
                .ToList();

            foreach (var ifaceUnit in ifaceUnits)
            {
                var implUnit = FindMatchingMethod(ifaceUnit, implUnits);
                if (implUnit is not null)
                    methodMap[ifaceUnit.Id] = implUnit.Id;
            }
        }

        return methodMap;
    }

    private static CodeUnit? FindMatchingMethod(CodeUnit ifaceUnit, List<CodeUnit> implUnits)
    {
        return implUnits.FirstOrDefault(iu =>
            iu.MethodName == ifaceUnit.MethodName
            && ParameterTypesEqual(iu.ParameterTypes, ifaceUnit.ParameterTypes));
    }

    private static bool ParameterTypesEqual(List<string> a, List<string> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    // Bean Anchor Method (representative node)
    // ═══════════════════════════════════════════════════════════════

    private static string? FindBeanAnchorMethod(BeanDef bean, IReadOnlyList<CodeUnit> allUnits)
    {
        return allUnits
            .Where(u => string.Equals(u.Namespace, bean.Namespace, StringComparison.Ordinal)
                        && u.ClassName == bean.ClassName)
            .OrderBy(u => u.MethodName, StringComparer.Ordinal)
            .Select(u => u.Id)
            .FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════════
    // Type Name Parsing
    // ═══════════════════════════════════════════════════════════════

    private static (string Namespace, string ClassName) ParseTypeName(string typeFullName)
    {
        // "MyApp.Services.OrderService, MyApp" → "MyApp.Services" + "OrderService"
        // "MyApp.Services.OrderService"         → "MyApp.Services" + "OrderService"
        var typePart = typeFullName.Split(',')[0].Trim();
        var lastDot = typePart.LastIndexOf('.');
        if (lastDot < 0)
            return ("", typePart);

        return (typePart[..lastDot], typePart[(lastDot + 1)..]);
    }

    private static string GetSimpleTypeName(TypeSyntax type)
    {
        return type switch
        {
            SimpleNameSyntax simple => simple.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => ""
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // SyntaxTree Helpers
    // ═══════════════════════════════════════════════════════════════

    private static SyntaxTree? ParseSourceFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var sourceText = File.ReadAllText(filePath);
            return CSharpSyntaxTree.ParseText(sourceText, path: filePath);
        }
        catch
        {
            return null;
        }
    }

    private static ClassDeclarationSyntax? FindClass(SyntaxNode root, string className)
    {
        return root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == className);
    }

    // ═══════════════════════════════════════════════════════════════
    // Path Utilities
    // ═══════════════════════════════════════════════════════════════

    private static string NormalizeFilePath(string fullPath, string scanRoot) =>
        Path.GetRelativePath(scanRoot, fullPath).Replace('\\', '/');

    private static bool IsUnderExcludedDirectory(string filePath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (ExcludedDirNames.Contains(segment))
                return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // Internal Types
    // ═══════════════════════════════════════════════════════════════

    private sealed record BeanDef(
        string Id,
        string TypeFullName,
        string Namespace,
        string ClassName,
        List<PropRef> PropertyRefs,
        string ConfigFilePath);

    private sealed record PropRef(
        string Name,
        string RefBeanId);

    private sealed record InterfaceImplMapping(
        string IfaceClassName,
        string IfaceNamespace,
        string ImplClassName,
        string ImplNamespace);
}
