namespace Core.Graph.Analysis.GenericResolution;

internal static class NamePatterns
{
    public static bool IsGenericParameter(string typeName)
    {
        if (typeName.Length == 0 || !typeName.StartsWith("T", StringComparison.Ordinal))
            return false;
        if (typeName.Length == 1)
            return true;
        foreach (var c in typeName[1..])
            if (!char.IsDigit(c))
                return false;
        return true;
    }

    public static bool IsInterfaceDaoPattern(string typeName)
    {
        if (typeName.Length < 5)
            return false;
        if (!typeName.StartsWith("ID", StringComparison.Ordinal))
            return false;
        if (!typeName.EndsWith("Dao", StringComparison.OrdinalIgnoreCase))
            return false;
        return char.IsUpper(typeName[2]);
    }
}
