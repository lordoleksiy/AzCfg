using AzCfg.Core.Model;

namespace AzCfg.Core.Abstractions;

public interface ILocalTarget
{
    string Kind { get; }
    bool CanHandle(ProjectInfo project);
    Task<IReadOnlyDictionary<string, string>> ReadAsync(ProjectInfo project, CancellationToken ct);
    Task WriteAsync(ProjectInfo project, IReadOnlyList<ConfigEntry> entries, CancellationToken ct);
}
