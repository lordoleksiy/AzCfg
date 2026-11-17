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
/// is an error naming <c>--service</c>. Human output lists warnings and ignored keys. A blocked pull exits with 1.
/// </summary>
public abstract class PullCommandBase<TSettings>(
    IProjectService projects,
    ISyncService sync,
    IUserStateStore state,
    IEnvironmentResolver resolver,
    IAnsiConsole console) : EnvironmentCommandBase<TSettings>(projects, state, resolver) where TSettings : PullSettings
{
    protected override async Task<ProjectBinding> GetBindingAsync(string repo, ProjectResolution resolution, TSettings settings, CancellationToken ct) =>
        resolution.Existing ?? await BindAsync(repo, resolution, settings.Service, !settings.Json && !settings.Yes, ct);

    public override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken ct)
    {
        var (binding, env) = await ResolveAsync(settings, ct);

        var result = settings.Json
            ? await sync.PullAsync(binding, env, null, settings.Force, ct)
            : await console.Status().StartAsync($"Pulling {env.Name}...", status =>
                sync.PullAsync(binding, env, new Progress<SyncProgress>(p => status.Status($"{p.Stage} {p.Done}/{p.Total}")), settings.Force, ct));

        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(result));
        }
        else if (result.Blocked)
        {
            console.MarkupLine("[yellow]Pull blocked: local changes would be lost.[/]");
            result.LocalChanges.ToList().ForEach(k => console.MarkupLineInterpolated($"  {k}"));
            console.MarkupLine("Re-run with [bold]--force[/] to overwrite them.");
        }
        else
        {
            var table = new Table().AddColumns("Environment", "Values", "Secrets");
            table.AddRow(Markup.Escape(result.Environment), result.ValuesWritten.ToString(), result.SecretsWritten.ToString());
            console.Write(table);
        }

        if (!settings.Json)
        {
            WarningsView.Render(console, result.Warnings);
            if (result.Ignored.Count > 0)
            {
                console.MarkupLineInterpolated($"{result.Ignored.Count} ignored key(s) kept local: {string.Join(", ", result.Ignored)}");
            }
        }

        return result.Blocked ? 1 : 0;
    }

    private async Task<ProjectBinding> BindAsync(string repo, ProjectResolution resolution, string? requested, bool interactive, CancellationToken ct)
    {
        var candidates = resolution.Candidates;
        var service = requested ?? (candidates.Count, resolution.IsConfident, interactive) switch
        {
            (0, _, _) => throw new InvalidOperationException("No service keys were found in the App Configuration store for this project; pass --service <name>."),
            (_, true, _) => candidates[0],
            (_, _, true) => console.Prompt(new SelectionPrompt<string>().Title("Service:").AddChoices(candidates)),
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
        return await Projects.BindAsync(repo, service, values, secrets, ct);
    }

    private LocalTargetBinding Choose(string title, IEnumerable<LocalTargetBinding> options) =>
        console.Prompt(new SelectionPrompt<LocalTargetBinding>().Title(title).AddChoices(options).UseConverter(t => Markup.Escape($"{t.Kind} ({t.ProjectPath})")));
}
