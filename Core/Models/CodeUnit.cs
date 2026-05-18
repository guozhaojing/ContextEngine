using Core.Semantics;

namespace Core.Models;

public class CodeUnit
{
    public string Id { get; set; } = "";

    public string FilePath { get; set; } = "";

    public string RelativeFilePath { get; set; } = "";

    public string ProjectName { get; set; } = "";

    public string ProjectPath { get; set; } = "";

    public string Namespace { get; set; } = "";

    public string ClassName { get; set; } = "";

    public string MethodName { get; set; } = "";

    public string Content { get; set; } = "";

    public List<string> Calls { get; set; } = new();

    public List<ResolvedMethodInfo> ResolvedCalls { get; set; } = new();
}
