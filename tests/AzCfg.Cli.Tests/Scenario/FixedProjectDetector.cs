using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>Detects a fixed list of projects regardless of the path (no file system access).</summary>
public sealed class FixedProjectDetector(params ProjectInfo[] projects) : IProjectDetector
{
    public Task<IReadOnlyList<ProjectInfo>> DetectAsync(string repoPath, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProjectInfo>>(projects);
}
