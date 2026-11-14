using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Pulls the environment (<c>--env</c>, else the binding's active one, else <c>dev</c>) into the local targets.
/// Prompts for service and targets only when the project is not bound yet and neither <c>--json</c> nor <c>--yes</c> is set;
/// otherwise an ambiguous project is an error. A blocked pull exits with 1.
/// </summary>
public sealed class PullCommand(
    IProjectService projects,
    ISyncService sync,
    IUserStateStore state,
    IEnvironmentResolver resolver,
    IAnsiConsole console) : AsyncCommand<PullSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PullSettings settings, CancellationToken ct)
    {
        var profile = await state.LoadProfileAsync(ct) ?? throw new InvalidOperationException("No profile found. Run 'azcfg init' first.");
        var repo = Path.GetFullPath(settings.Project);
        var resolution = await projects.ResolveAsync(repo, ct);
        var binding = resolution.Existing ?? await BindAsync(repo, resolution, !settings.Json && !settings.Yes, ct);
        var name = settings.Env ?? binding.ActiveEnvironment ?? "dev";
        var env = resolver.Resolve(profile).FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Environment '{name}' is not in the profile.");

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

        return result.Blocked ? 1 : 0;
    }

    private async Task<ProjectBinding> BindAsync(string repo, ProjectResolution resolution, bool interactive, CancellationToken ct)
    {
        var service = resolution.IsConfident ? resolution.Candidates[0]
            : interactive && resolution.Candidates.Count > 0 ? console.Prompt(new SelectionPrompt<string>().Title("Service:").AddChoices(resolution.Candidates))
            : throw new InvalidOperationException("Cannot determine the service for this project; run 'azcfg pull' interactively once.");

        var safe = resolution.TargetOptions.Where(o => o.IsSafeForSecrets).Select(o => o.Target).ToList();
        if (safe.Count == 0)
        {
            throw new InvalidOperationException("No local target is safe for secrets (use user-secrets or a git-ignored file).");
        }

        var (values, secrets) = interactive
            ? (Choose("Local target for values:", resolution.TargetOptions.Select(o => o.Target)), Choose("Local target for secrets:", safe))
            : (safe[0], safe[0]);
        return await projects.BindAsync(repo, service, values, secrets, ct);
    }

    private LocalTargetBinding Choose(string title, IEnumerable<LocalTargetBinding> options) =>
        console.Prompt(new SelectionPrompt<LocalTargetBinding>().Title(title).AddChoices(options).UseConverter(t => Markup.Escape($"{t.Kind} ({t.ProjectPath})")));
}
