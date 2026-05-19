using Microsoft.CodeAnalysis;

// =============================================================================
// Semantics/SymbolHandle.cs — stable, serializable reference to an ISymbol
// =============================================================================
// Uses Roslyn's DocumentationCommentId as the stable identifier.
// This is the ONE identity source for all graph nodes — no string fullname identity.
// =============================================================================

namespace Core.Semantics;

public readonly struct SymbolHandle : IEquatable<SymbolHandle>
{
    public SymbolHandle(string documentationCommentId, string? containingAssembly = null, SymbolHandleKind kind = SymbolHandleKind.Unknown)
    {
        DocumentationCommentId = documentationCommentId ?? throw new ArgumentNullException(nameof(documentationCommentId));
        ContainingAssembly = containingAssembly ?? "";
        Kind = kind;
    }

    public string DocumentationCommentId { get; }

    public string ContainingAssembly { get; }

    public SymbolHandleKind Kind { get; }

    public bool IsEmpty => string.IsNullOrEmpty(DocumentationCommentId);

    public string Value => string.IsNullOrEmpty(ContainingAssembly)
        ? DocumentationCommentId
        : $"{ContainingAssembly}|{DocumentationCommentId}";

    public bool Equals(SymbolHandle other) =>
        StringComparer.Ordinal.Equals(DocumentationCommentId, other.DocumentationCommentId);

    public override bool Equals(object? obj) => obj is SymbolHandle other && Equals(other);

    public override int GetHashCode() => DocumentationCommentId.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => IsEmpty ? "<empty>" : Value;

    public static bool operator ==(SymbolHandle left, SymbolHandle right) => left.Equals(right);

    public static bool operator !=(SymbolHandle left, SymbolHandle right) => !left.Equals(right);

    public static readonly SymbolHandle Empty = default;

    public static SymbolHandle Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Empty;

        var parts = text.Split('|', 2);
        if (parts.Length == 2)
            return new SymbolHandle(parts[1], parts[0], InferKind(parts[1]));
        return new SymbolHandle(text, "", InferKind(text));
    }

    public static bool TryParse(string? text, out SymbolHandle handle)
    {
        handle = Empty;
        if (string.IsNullOrEmpty(text))
            return false;
        handle = Parse(text);
        return true;
    }

    private static SymbolHandleKind InferKind(string documentationCommentId)
    {
        if (string.IsNullOrEmpty(documentationCommentId))
            return SymbolHandleKind.Unknown;

        return documentationCommentId[0] switch
        {
            'M' => SymbolHandleKind.Method,
            'T' => SymbolHandleKind.Type,
            'P' => SymbolHandleKind.Property,
            'F' => SymbolHandleKind.Field,
            'N' => SymbolHandleKind.Namespace,
            'E' => SymbolHandleKind.Event,
            _ => SymbolHandleKind.Unknown
        };
    }

    public static SymbolHandle FromMethod(IMethodSymbol method) => new(
        method.GetDocumentationCommentId() ?? "",
        method.ContainingAssembly?.Name,
        SymbolHandleKind.Method);

    public static SymbolHandle FromType(INamedTypeSymbol type) => new(
        type.GetDocumentationCommentId() ?? "",
        type.ContainingAssembly?.Name,
        SymbolHandleKind.Type);

    public static SymbolHandle FromProperty(IPropertySymbol property) => new(
        property.GetDocumentationCommentId() ?? "",
        property.ContainingAssembly?.Name,
        SymbolHandleKind.Property);

    public static SymbolHandle FromField(IFieldSymbol field) => new(
        field.GetDocumentationCommentId() ?? "",
        field.ContainingAssembly?.Name,
        SymbolHandleKind.Field);
}

public enum SymbolHandleKind
{
    Unknown = 0,
    Method = 1,
    Type = 2,
    Property = 3,
    Field = 4,
    Namespace = 5,
    Event = 6,
    Entity = 7,
}
