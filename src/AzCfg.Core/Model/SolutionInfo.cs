namespace AzCfg.Core.Model;

/// <summary>A solution file found in a workspace; <see cref="ProjectPaths"/> are absolute paths of its .csproj entries.</summary>
public sealed record SolutionInfo(string Path, string Name, SolutionFormat Format, IReadOnlyList<string> ProjectPaths);
