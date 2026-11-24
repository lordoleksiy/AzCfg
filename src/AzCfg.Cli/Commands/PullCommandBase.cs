using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Pulls the requested environment (else the binding's active one, else <c>dev</c>) into the local targets.
/// An unbound project is bound to <c>--service</c> when given (no detection), else to the detected service. Prompts for service and
/// targets only when the project is not bound yet and neither <c>--json</c> nor <c>--yes</c> is set; otherwise an ambiguous project
/// is an error naming <c>--service</c>. The interactive wizard finally offers to save the binding as <c>azcfg.json</c> for the team
/// (<c>project.shared</c>, default no); <c>--yes</c>/<c>--json</c> never write it. A project with a shared <c>azcfg.json</c> is bound from
/// it without the wizard (Core); when the manifest switched the service, the human output says so (<c>config list --json</c> shows the
/// result). Human output lists warnings and ignored keys. A blocked pull exits with 1.
/// </summary>
public abstract class PullCommandBase<TSettings>(
    IProjectService projects,
    IProjectSettingsService projectSettings,
    ISyncService sync,
    IUserStateStore state,
    IEnvironmentResolver resolver,
    IAnsiConsole console) : EnvironmentCommandBase<TSettings>(projects, state, resolver, console) where TSettings : PullSettings
{
    protected ISyncService Sync { get; } = sync;

    protected override async Task<ProjectBinding> GetBindingAsync(string repo, ProjectResolution resolution, TSettings settings, CancellationToken ct)
    {
        return resolution.Existing is { } existing
            ? Guard(existing, settings.Service)
            : await BindAsync(repo, resolution, settings.Service, !settings.Json && !settings.Yes, ct);
    }

    /// <summary>A bound project keeps its service: <c>--service</c> naming another one is an error (ADR 0014), the same one is a no-op.</summary>
    private static ProjectBinding Guard(ProjectBinding existing, string? requested)
    {
        ProjectServiceGuard.EnsureSameService(existing, requested);
        return existing;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken ct)
    {
        var (binding, env) = await ResolveAsync(settings, ct);

        var result = await PullAsync(binding, env, settings, ct);

        if (settings.Json)
        {
            Console.Profile.Out.Writer.WriteLine(CliJson.Serialize(result));
        }
        else if (result.Blocked)
        {
            Console.MarkupLine("[yellow]Pull blocked: local changes would be lost.[/]");
            result.LocalChanges.ToList().ForEach(k => Console.MarkupLineInterpolated($"  {k}"));
            Console.MarkupLine("Re-run with [bold]--force[/] to overwrite them.");
        }
        else
        {
            var table = new Table().AddColumns("Environment", "Values", "Secrets");
            table.AddRow(Markup.Escape(result.Environment), result.ValuesWritten.ToString(), result.SecretsWritten.ToString());
            Console.Write(table);
        }

        if (!settings.Json)
        {
            WarningsView.Render(Console, result.Warnings);
            if (result.Ignored.Count > 0)
            {
                Console.MarkupLineInterpolated($"{result.Ignored.Count} ignored key(s) kept local: {string.Join(", ", result.Ignored)}");
            }
        }

        return result.Blocked ? 1 : 0;
    }

    /// <summary>Runs the pull itself; a derived command overrides it to pull a subset of the keys.</summary>
    protected virtual async Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, TSettings settings, CancellationToken ct) =>
        settings.Json
            ? await Sync.PullAsync(binding, env, null, settings.Force, ct)
            : await Console.Status().StartAsync($"Pulling {env.Name}...", status =>
                Sync.PullAsync(binding, env, new Progress<SyncProgress>(p => status.Status($"{p.Stage} {p.Done}/{p.Total}")), settings.Force, ct));

    private async Task<ProjectBinding> BindAsync(string repo, ProjectResolution resolution, string? requested, bool interactive, CancellationToken ct)
    {
        var candidates = resolution.Candidates;
        var service = requested ?? (candidates.Count, resolution.IsConfident, interactive) switch
        {
            (0, _, _) => throw new InvalidOperationException("No service keys were found in the App Configuration store for this project; pass --service <name>."),
            (_, true, _) => candidates[0],
            (_, _, true) => Console.Prompt(new SelectionPrompt<string>().Title("Service:").AddChoices(candidates)),
            (1, _, _) => throw new InvalidOperationException($"The only service in the App Configuration store ({candidates[0]}) does not match this project; pass --service <name> or run interactively."),
            _ => throw new InvalidOperationException($"Several services match this project ({string.Join(", ", candidates)}); pass --service <name> or run interactively."),
        };

        var safe = resolution.TargetOptions.Where(o => o.IsSafeForSecrets).Select(o => o.Target).ToList();
        if (safe.Count == 0)
        {
            throw new InvalidOperationException("No local target is safe for secrets (use user-secrets or a git-ignored file).");
        }

        var (values, secrets) = interactive
            ? (Choose("Local target for values:", resolution.TargetOptions.Select(o => o.Target)), Choose("Local target for secrets:", safe))
            : (safe[0], safe[0]);
        var binding = await Projects.BindAsync(repo, service, values, secrets, ct);
        if (interactive && Console.Confirm("Save as azcfg.json for the team (service and targets, no values)?", false))
        {
            // Sharing is optional: a failure (e.g. a target outside the project folder, an unwritable file) never stops the pull.
            try
            {
                var shared = await projectSettings.SetAsync(repo, ProjectSettingKeys.Shared, "true", ct);
                Console.MarkupLineInterpolated($"Saved {shared.Source}. {shared.Hint}");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Console.MarkupLineInterpolated($"[yellow]Could not save azcfg.json: {CliErrors.Describe(e)}[/] Retry later with 'azcfg config set project.shared true'.");
            }
        }

        return binding;
    }

    private LocalTargetBinding Choose(string title, IEnumerable<LocalTargetBinding> options) =>
        Console.Prompt(new SelectionPrompt<LocalTargetBinding>().Title(title).AddChoices(options).UseConverter(t => Markup.Escape($"{t.Kind} ({t.ProjectPath})")));
}
