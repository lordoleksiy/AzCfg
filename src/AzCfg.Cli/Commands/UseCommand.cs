using AzCfg.Core.Abstractions;
using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Pulls the given environment and makes it the project's active one; a blocked pull leaves the active environment unchanged.
/// Guards run before anything is written: local changes that would be overwritten (lists the keys, exit 1; <c>--force</c> overrides),
/// a protected environment (confirmation by typing its name, or <c>--confirm &lt;ENV&gt;</c>) and the first activation with unmanaged
/// local keys (confirmation, or <c>--yes</c>). <c>--json</c> never prompts: a refusal is written as <see cref="UseRefusal"/> with exit 1.
/// </summary>
public sealed class UseCommand : PullCommandBase<UseSettings>
{
    private readonly ISyncService sync;
    private readonly IAnsiConsole console;

    public UseCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IAnsiConsole console)
        : base(projects, sync, state, resolver, console)
    {
        this.sync = sync;
        this.console = console;
    }

    protected override string? RequestedEnv(UseSettings settings) => settings.Name;

    public override async Task<int> ExecuteAsync(CommandContext context, UseSettings settings, CancellationToken ct)
    {
        var (binding, env) = await ResolveAsync(settings, ct);

        if (!settings.Force && await BlockedKeysAsync(binding, ct) is { Count: > 0 } blocked)
        {
            return Refuse(settings, new(env.Name, "blocked", blocked, "Push them (azcfg push), keep the remote value (azcfg pull --key <KEY> --force) or overwrite everything with --force."), true);
        }

        if (EnvironmentRules.KindOf(env) == EnvironmentKind.Production && !ConfirmedProtected(settings, env.Name))
        {
            return Refuse(settings, new(env.Name, "protected", [], $"Secrets of '{env.Name}' will be written to local files; pass --confirm {env.Name}."), false);
        }

        if (!settings.Yes && await sync.UnmanagedLocalKeysAsync(binding, ct) is { Count: > 0 } unmanaged)
        {
            var refusal = new UseRefusal(env.Name, "unmanaged", unmanaged, "These local keys are not managed by azcfg yet and will be overwritten; pass --yes to continue.");
            if (settings.Json)
            {
                return Refuse(settings, refusal, false);
            }

            console.MarkupLineInterpolated($"[yellow]First activation: {unmanaged.Count} local key(s) are not managed by azcfg and will be overwritten:[/]");
            unmanaged.ToList().ForEach(k => console.MarkupLineInterpolated($"  {k}"));
            if (!console.Confirm("Continue?", false))
            {
                console.MarkupLine("Cancelled.");
                return 1;
            }
        }

        return await base.ExecuteAsync(context, settings, ct);
    }

    /// <summary>Local changes of the currently active environment that a pull would overwrite; none for a first activation or an active environment no longer in the profile.</summary>
    private async Task<IReadOnlyList<string>> BlockedKeysAsync(ProjectBinding binding, CancellationToken ct)
    {
        if (binding.ActiveEnvironment is not { } active)
        {
            return [];
        }

        try
        {
            return (await sync.StatusAsync(binding, await FindEnvironmentAsync(active, ct), ct)).BlockedKeys;
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private bool ConfirmedProtected(UseSettings settings, string env)
    {
        if (settings.Confirm is not null)
        {
            return string.Equals(settings.Confirm, env, StringComparison.OrdinalIgnoreCase);
        }

        if (settings.Json)
        {
            return false;
        }

        console.MarkupLineInterpolated($"[yellow]Secrets of {env} will be written to local files.[/]");
        return string.Equals(console.Prompt(new TextPrompt<string>($"Type [bold]{Markup.Escape(env)}[/] to continue:")), env, StringComparison.OrdinalIgnoreCase);
    }

    private int Refuse(UseSettings settings, UseRefusal refusal, bool listKeys)
    {
        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(refusal));
            return 1;
        }

        if (listKeys)
        {
            console.MarkupLineInterpolated($"[yellow]Use blocked: local changes would be overwritten.[/]");
            refusal.Keys.ToList().ForEach(k => console.MarkupLineInterpolated($"  {k}"));
        }

        console.MarkupLineInterpolated($"{refusal.Hint}");
        return 1;
    }
}
