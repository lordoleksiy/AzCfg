using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeLocalTarget(LocalTargetKind kind, bool canHandle = true, bool isSafeForSecrets = true) : ILocalTarget
{
    public LocalTargetKind Kind => kind;
    public List<(LocalTargetBinding Target, IReadOnlyList<LocalValue> Values)> Writes { get; } = [];
    public IReadOnlyList<LocalValue> Contents { get; set; } = [];
    public int Reads { get; private set; }
    public List<LocalTargetBinding> EnsureGitIgnoredCalls { get; } = [];
    public bool MakesSafeWhenGitIgnored { get; set; }
    public bool CanHandle(ProjectInfo project) => canHandle;

    public Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct) =>
        Task.FromResult(new LocalTargetOption(target, true, isSafeForSecrets || (MakesSafeWhenGitIgnored && EnsureGitIgnoredCalls.Count > 0)));

    public Task EnsureGitIgnoredAsync(LocalTargetBinding target, CancellationToken ct)
    {
        EnsureGitIgnoredCalls.Add(target);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LocalValue>> ReadAsync(LocalTargetBinding target, CancellationToken ct)
    {
        Reads++;
        return Task.FromResult(Contents);
    }

    public Task WriteAsync(LocalTargetBinding target, IReadOnlyList<LocalValue> values, CancellationToken ct)
    {
        Writes.Add((target, values));
        return Task.CompletedTask;
    }
}
