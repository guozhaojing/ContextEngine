// =============================================================================
// Semantics/SemanticSymbolResolver.cs — resolves ISymbol from Roslyn
// =============================================================================
// Given a SyntaxNode and SemanticModel, resolves the ISymbol and creates a stable
// SymbolHandle. This is the primary entry point for symbol-grounded graph building.
//
// Handles:
//   - Method symbols (including extension methods via ReducedFrom)
//   - Type symbols for class/struct/interface declarations
//   - Overload resolution (parameter type disambiguation)
//   - Generic type parameter binding
// =============================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Semantics;

public static class SemanticSymbolResolver
{
    public static SymbolHandle ResolveMethodHandle(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var symbol = ResolveMethodSymbol(invocation, semanticModel, cancellationToken);
        return symbol is not null ? SymbolHandle.FromMethod(symbol) : SymbolHandle.Empty;
    }

    public static SymbolHandle ResolveTypeHandle(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var symbol = semanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
        return symbol is INamedTypeSymbol typeSymbol
            ? SymbolHandle.FromType(typeSymbol)
            : SymbolHandle.Empty;
    }

    public static SymbolHandle ResolveTypeHandle(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
        return symbol is INamedTypeSymbol typeSymbol
            ? SymbolHandle.FromType(typeSymbol)
            : SymbolHandle.Empty;
    }

    public static SymbolHandle ResolveDeclaredMethodHandle(
        MethodDeclarationSyntax methodDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var symbol = semanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken);
        return symbol is IMethodSymbol methodSymbol
            ? SymbolHandle.FromMethod(methodSymbol)
            : SymbolHandle.Empty;
    }

    public static SymbolHandle ResolvePropertyHandle(
        IPropertySymbol propertySymbol) =>
        SymbolHandle.FromProperty(propertySymbol);

    public static IMethodSymbol? ResolveMethodSymbol(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        var symbol = symbolInfo.Symbol;

        if (symbol is null && symbolInfo.CandidateSymbols.Length > 0)
            symbol = symbolInfo.CandidateSymbols[0];

        if (symbol is IMethodSymbol method)
        {
            if (method.MethodKind == MethodKind.ReducedExtension && method.ReducedFrom is not null)
                return method.ReducedFrom;
            return method;
        }

        var expressionInfo = semanticModel.GetSymbolInfo(invocation.Expression, cancellationToken);
        var exprSymbol = expressionInfo.Symbol ?? expressionInfo.CandidateSymbols.FirstOrDefault();

        return exprSymbol switch
        {
            IMethodSymbol exprMethod => exprMethod,
            IFieldSymbol { Type: { TypeKind: TypeKind.Delegate } } field
                when field.Type is INamedTypeSymbol delegateType
                => delegateType.DelegateInvokeMethod,
            _ => null
        };
    }

    public static INamedTypeSymbol? ResolveTypeSymbol(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
        return symbol as INamedTypeSymbol;
    }

    public static bool IsOverloaded(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        return symbolInfo.CandidateSymbols.Length > 1;
    }

    public static IReadOnlyList<IMethodSymbol> GetOverloadCandidates(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        return symbolInfo.CandidateSymbols
            .OfType<IMethodSymbol>()
            .ToList()
            .AsReadOnly();
    }

    public static bool IsExtensionMethodInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var symbol = ResolveMethodSymbol(invocation, semanticModel, cancellationToken);
        return symbol?.MethodKind == MethodKind.ReducedExtension
            || (symbol?.IsExtensionMethod == true);
    }
}
