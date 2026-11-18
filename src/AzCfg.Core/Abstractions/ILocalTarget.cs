using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Abstractions;

public interface ILocalTarget
{
    LocalTargetKind Kind { get; }
    bool CanHandle(ProjectInfo project);
    Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct);
    Task<IReadOnlyList<LocalValue>> ReadAsync(LocalTargetBinding target, CancellationToken ct);
    Task WriteAsync(LocalTargetBinding target, IReadOnlyList<LocalValue> values, CancellationToken ct);

    /// <summary>
    /// Makes the target's file git-ignored when it lives in a repository (ADR 0009); a no-op when it is already ignored
    /// or lives outside the repo. Called by Core only in the "gitignoreLocalFiles" mode.
    /// </summary>
    Task EnsureGitIgnoredAsync(LocalTargetBinding target, CancellationToken ct);
}
