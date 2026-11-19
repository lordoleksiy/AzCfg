using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Pulls <c>--env</c>, else the active environment, else <c>dev</c>. With <c>--key</c> only the matching keys are pulled
/// (<see cref="ISyncService.PullSelectedAsync"/>): active environment only; keys with unpushed local changes block the pull
/// unless <c>--force</c> (keep remote for those keys). Keys added locally and never pushed are left alone.
/// </summary>
public sealed class PullCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IAnsiConsole console)
    : PullCommandBase<PullFilesSettings>(projects, sync, state, resolver, console)
{
    protected override async Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, PullFilesSettings settings, CancellationToken ct)
    {
        if (settings.Key.Length == 0)
        {
            return await base.PullAsync(binding, env, settings, ct);
        }

        if (!string.Equals(binding.ActiveEnvironment, env.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(binding.ActiveEnvironment is null
                ? $"--key pulls into the active environment, but the project has none; run 'azcfg use {env.Name}' first."
                : $"--key works on the active environment ('{binding.ActiveEnvironment}') only, not '{env.Name}'; run 'azcfg use {env.Name}' first.");
        }

        var items = (await Sync.DiffAsync(binding, env, ct)).Items
            .Where(i => KeySelector.IsSelected(settings.Key, i.Key, (i.Local ?? i.Remote ?? i.Base)?.Scope ?? ConfigScope.Service))
            .ToList();
        if (items.Count == 0)
        {
            throw new InvalidOperationException($"No key matches {string.Join(", ", settings.Key.Select(k => $"'{k}'"))}.");
        }

        // A key added locally and never pushed has nothing to pull: it neither blocks the pull nor is removed by --force.
        items.RemoveAll(i => i.Kind == DiffKind.LocalAdded);
        var changed = items.Where(i => i.Kind is DiffKind.LocalModified or DiffKind.LocalDeleted or DiffKind.Conflict)
            .Select(i => i.Key).Order(StringComparer.Ordinal).ToList();
        if (changed.Count > 0 && !settings.Force)
        {
            return new PullResult(env.Name, 0, 0, true, changed);
        }

        return await Sync.PullSelectedAsync(binding, env, [.. items.Select(i => i.Key)], ct);
    }
}
