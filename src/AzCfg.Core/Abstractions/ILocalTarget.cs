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
}
