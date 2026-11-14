using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeLocalTarget(LocalTargetKind kind, bool canHandle = true, bool isSafeForSecrets = true) : ILocalTarget
{
    public LocalTargetKind Kind => kind;
    public bool CanHandle(ProjectInfo project) => canHandle;

    public Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct) =>
        Task.FromResult(new LocalTargetOption(target, true, isSafeForSecrets));

    public Task<IReadOnlyList<LocalValue>> ReadAsync(LocalTargetBinding target, CancellationToken ct) => throw new NotSupportedException();
    public Task WriteAsync(LocalTargetBinding target, IReadOnlyList<LocalValue> values, CancellationToken ct) => throw new NotSupportedException();
}
