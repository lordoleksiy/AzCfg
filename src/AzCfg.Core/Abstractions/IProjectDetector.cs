using AzCfg.Core.Model;

namespace AzCfg.Core.Abstractions;

public interface IProjectDetector
{
    /// <summary>Finds .NET projects under <paramref name="repoPath"/>. Implementations decide the search strategy.</summary>
    Task<IReadOnlyList<ProjectInfo>> DetectAsync(string repoPath, CancellationToken ct);
}
