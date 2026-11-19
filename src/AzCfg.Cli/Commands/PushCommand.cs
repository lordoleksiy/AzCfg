using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Plans and applies the local files to <c>--env</c> (else the active environment, else <c>dev</c>): add and update only by default,
/// <c>--delete</c> also deletes remote keys deleted locally. Conflicts refuse the push (exit 1).
/// A needed Key Vault is chosen interactively and saved in the binding. Confirmation is skipped by <c>--yes</c>, except for a protected
/// environment, which needs its name (prompt or <c>--confirm</c>); <c>--json</c> never prompts and throws when a confirmation or vault choice is missing.
/// Failed keys exit 1.
/// </summary>
public sealed class PushCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IResourceDiscovery discovery, IAnsiConsole console)
    : PushCommandBase<PushFilesSettings>(projects, sync, state, resolver, discovery, console)
{
    public override async Task<int> ExecuteAsync(CommandContext context, PushFilesSettings settings, CancellationToken ct)
    {
        var (binding, env) = await ResolveAsync(settings, ct);
        return await PushAsync(settings, binding, env, new PushSource.LocalFiles(settings.Delete), ct);
    }

    protected override async Task<SyncPlan> PlanAsync(PushFilesSettings settings, ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct)
    {
        var plan = await base.PlanAsync(settings, binding, env, source, ct);
        if (settings.Key.Length == 0)
        {
            return plan;
        }

        var selected = KeyFilter.Restrict(plan, settings.Key);
        if (!settings.Force || selected.Conflicts.Count == 0)
        {
            return selected;
        }

        if (!string.Equals(binding.ActiveEnvironment, env.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"--force resolves conflicts of the active environment only, not '{env.Name}'; run 'azcfg use {env.Name}' first.");
        }

        // Keep local is planned in memory: the snapshot changes only when the plan is applied (a declined or failed push leaves the conflict).
        var keepLocal = selected.Conflicts.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var resolved = source is PushSource.LocalFiles files ? files with { KeepLocal = keepLocal } : source;
        return KeyFilter.Restrict(await base.PlanAsync(settings, binding, env, resolved, ct), settings.Key);
    }
}
