// =============================================================================
// Semantics/TypeBindingResolver.cs — resolves generic type bindings
// =============================================================================
// Determines concrete type arguments for generic base classes and interfaces.
// Used to resolve:
//   - BaseBLL<ConcreteEntity> → ConcreteEntity
//   - IDao<Entity, KeyType> → Entity, KeyType
//   - Spring beans implementing generic interfaces
//   - Any class→entity binding via inheritance chain
// =============================================================================

using Core.Graph.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Semantics;

public static class TypeBindingResolver
{
    public sealed record TypeBinding(
        string ResolvedTypeName,
        string ResolvedNamespace,
        string? ResolvedAssembly,
        SymbolHandle TypeHandle,
        ResolutionConfidence Confidence);

    public static IReadOnlyList<TypeBinding> ResolveBaseTypeGenericArguments(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TypeBinding>();
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken) as INamedTypeSymbol;
        if (classSymbol is null) return results;

        var baseType = classSymbol.BaseType;
        while (baseType is not null && baseType.SpecialType != SpecialType.System_Object)
        {
            if (baseType.TypeArguments.Length > 0)
            {
                foreach (var typeArg in baseType.TypeArguments)
                {
                    if (typeArg is INamedTypeSymbol namedArg)
                    {
                        results.Add(new TypeBinding(
                            namedArg.Name,
                            namedArg.ContainingNamespace.ToDisplayString(),
                            namedArg.ContainingAssembly?.Name,
                            SymbolHandle.FromType(namedArg),
                            ResolveConfidence(namedArg)
                        ));
                    }
                }
            }
            baseType = baseType.BaseType;
        }

        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (iface.TypeArguments.Length > 0)
            {
                foreach (var typeArg in iface.TypeArguments)
                {
                    if (typeArg is INamedTypeSymbol namedArg)
                    {
                        var alreadyAdded = results.Any(r =>
                            StringComparer.Ordinal.Equals(r.TypeHandle.Value, SymbolHandle.FromType(namedArg).Value));
                        if (!alreadyAdded)
                        {
                            results.Add(new TypeBinding(
                                namedArg.Name,
                                namedArg.ContainingNamespace.ToDisplayString(),
                                namedArg.ContainingAssembly?.Name,
                                SymbolHandle.FromType(namedArg),
                                ResolveConfidence(namedArg)
                            ));
                        }
                    }
                }
            }
        }

        return results;
    }

    public static TypeBinding? ResolveSingleBaseTypeGenericArgument(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var bindings = ResolveBaseTypeGenericArguments(classDeclaration, semanticModel, cancellationToken);
        return bindings.FirstOrDefault();
    }

    public static TypeBinding? ResolveTypedGenericArgument(
        ClassDeclarationSyntax classDeclaration,
        string baseClassName,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken) as INamedTypeSymbol;
        if (classSymbol is null) return null;

        var baseType = classSymbol.BaseType;
        while (baseType is not null)
        {
            if (StringComparer.Ordinal.Equals(baseType.Name, baseClassName)
                && baseType.TypeArguments.Length > 0)
            {
                var firstArg = baseType.TypeArguments[0];
                if (firstArg is INamedTypeSymbol namedArg)
                {
                    return new TypeBinding(
                        namedArg.Name,
                        namedArg.ContainingNamespace.ToDisplayString(),
                        namedArg.ContainingAssembly?.Name,
                        SymbolHandle.FromType(namedArg),
                        ResolveConfidence(namedArg)
                    );
                }
            }
            baseType = baseType.BaseType;
        }

        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (StringComparer.Ordinal.Equals(iface.Name, baseClassName)
                && iface.TypeArguments.Length > 0)
            {
                var firstArg = iface.TypeArguments[0];
                if (firstArg is INamedTypeSymbol namedArg)
                {
                    return new TypeBinding(
                        namedArg.Name,
                        namedArg.ContainingNamespace.ToDisplayString(),
                        namedArg.ContainingAssembly?.Name,
                        SymbolHandle.FromType(namedArg),
                        ResolveConfidence(namedArg)
                    );
                }
            }
        }

        return null;
    }

    public static bool IsAssignableTo(
        INamedTypeSymbol type,
        string targetBaseClassName,
        SemanticModel semanticModel)
    {
        var baseType = type.BaseType;
        while (baseType is not null)
        {
            if (StringComparer.Ordinal.Equals(baseType.Name, targetBaseClassName))
                return true;
            baseType = baseType.BaseType;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (StringComparer.Ordinal.Equals(iface.Name, targetBaseClassName))
                return true;
        }

        return false;
    }

    private static ResolutionConfidence ResolveConfidence(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.Locations.Any(l => l.IsInSource))
            return ResolutionConfidence.Exact;

        if (typeSymbol.Locations.Any(l => l.IsInMetadata))
            return ResolutionConfidence.High;

        return ResolutionConfidence.Medium;
    }
}
