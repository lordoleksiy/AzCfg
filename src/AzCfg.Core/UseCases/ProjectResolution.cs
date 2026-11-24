using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>
/// Result of resolving a project (decision 2). When <see cref="Existing"/> is not <c>null</c>, the client
/// uses it without asking — it is the developer's remembered choice (or, on first use, the binding created from the shared
/// <c>azcfg.json</c>). <see cref="ManifestPath"/> is that manifest when one is in effect (ADR 0012 amendment 2);
/// <see cref="PreviousService"/> is set when the manifest switched the binding to another service during this resolution
/// (the active environment was cleared and the snapshots dropped, as with <c>config set project.service</c>).
/// </summary>
public sealed record ProjectResolution(
    IReadOnlyList<string> Candidates,
    bool IsConfident,
    IReadOnlyList<LocalTargetOption> TargetOptions,
    ProjectBinding? Existing,
    string? ManifestPath = null,
    string? PreviousService = null);
