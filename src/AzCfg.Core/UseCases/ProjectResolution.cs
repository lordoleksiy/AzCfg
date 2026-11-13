using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

public sealed record ProjectResolution(
    IReadOnlyList<string> Candidates,
    bool IsConfident,
    IReadOnlyList<ProjectInfo> Targets);
