namespace AzCfg.Core.Model;

/// <summary>Result of <see cref="Abstractions.IWorkspaceScanner.ScanAsync"/>.</summary>
public sealed record WorkspaceScan(string RootPath, IReadOnlyList<SolutionInfo> Solutions, IReadOnlyList<WorkspaceProject> Projects);
