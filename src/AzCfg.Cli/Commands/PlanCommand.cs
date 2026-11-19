using AzCfg.Core.Abstractions;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Shows the changes, conflicts and warnings of pushing the local files to <c>--env</c> (else the active environment, else <c>dev</c>);
/// upsert by default, <c>--delete</c> also plans remote deletes. Warnings are shown even without changes. Needs a bound project; always exits 0.
/// </summary>
public sealed class PlanCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IAnsiConsole console)
    : EnvironmentCommandBase<PlanSettings>(projects, state, resolver)
{
    public override async Task<int> ExecuteAsync(CommandContext context, PlanSettings settings, CancellationToken ct)
    {
        var (binding, env) = await ResolveAsync(settings, ct);
        var plan = KeyFilter.Restrict(await sync.PlanPushAsync(binding, env, new PushSource.LocalFiles(settings.Delete), ct), settings.Key);

        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(plan));
            return 0;
        }

        if (plan.Changes.Count == 0 && plan.Conflicts.Count == 0)
        {
            console.MarkupLine("No changes.");
        }

        PlanView.Render(console, plan);
        return 0;
    }
}
