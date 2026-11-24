using AzCfg.Core.Model;

namespace AzCfg.Core.Abstractions;

/// <summary>Reads the keys of the git-tracked <c>appsettings*.json</c> files of a repository (ADR 0012, health check).</summary>
public interface ITrackedSettingsReader
{
    /// <summary>
    /// The flattened keys (<c>A:B</c>) of every git-tracked <c>appsettings*.json</c> under <paramref name="repoPath"/>.
    /// Values are read in memory only to compute <see cref="TrackedSettingKey.HasValue"/> and are never returned.
    /// Empty outside a git repository.
    /// </summary>
    Task<IReadOnlyList<TrackedSettingKey>> ReadKeysAsync(string repoPath, CancellationToken ct);
}
