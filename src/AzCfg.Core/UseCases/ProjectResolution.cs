using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>
/// Result of resolving a project (decision 2). When <see cref="Existing"/> is not <c>null</c>, the client
/// uses it without asking — it is the developer's remembered choice.
/// </summary>
public sealed record ProjectResolution(
    IReadOnlyList<string> Candidates,
    bool IsConfident,
    IReadOnlyList<LocalTargetOption> TargetOptions,
    ProjectBinding? Existing);
