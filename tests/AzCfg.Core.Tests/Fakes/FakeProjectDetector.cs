using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeProjectDetector(params ProjectInfo[] projects) : IProjectDetector
{
    public Task<IReadOnlyList<ProjectInfo>> DetectAsync(string repoPath, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProjectInfo>>(projects);
}
