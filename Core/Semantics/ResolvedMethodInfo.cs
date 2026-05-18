namespace Core.Semantics;

public class ResolvedMethodInfo
{
    public string Namespace { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    public bool IsExternal { get; set; }
}
