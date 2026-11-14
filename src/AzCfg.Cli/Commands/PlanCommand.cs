using AzCfg.Core.Abstractions;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Shows the changes and conflicts of pushing the local files to <c>--env</c> (else the active environment, else <c>dev</c>). Needs a bound project; always exits 0.</summary>
public sealed class PlanCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IAnsiConsole console)
    : EnvironmentCommandBase<GlobalSettings>(projects, state, resolver)
{
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken ct)
    {
        var (binding, env) = await ResolveAsync(settings, ct);
        var plan = await sync.PlanPushAsync(binding, env, new PushSource.LocalFiles(), ct);

        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(plan));
        }
        else if (plan.Changes.Count == 0 && plan.Conflicts.Count == 0)
        {
            console.MarkupLine("No changes.");
        }
        else
        {
            PlanView.Render(console, plan);
        }

        return 0;
    }
}
