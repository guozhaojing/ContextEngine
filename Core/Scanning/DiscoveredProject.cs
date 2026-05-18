namespace Core.Scanning;

public sealed record DiscoveredProject(
    string Name,
    string ProjectFilePath,
    string ProjectDirectory);
