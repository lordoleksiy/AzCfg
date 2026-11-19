using AzCfg.Core.Abstractions;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Shows where an environment (<c>--env</c>, else the active one, else <c>dev</c>) stands against the local files: local changes,
/// remote changes, conflicts, blocked keys, warnings and the ignored count. Keys and counters only, never values. Needs a bound project; always exits 0.
/// </summary>
public sealed class StatusCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IAnsiConsole console)
    : EnvironmentCommandBase<StatusSettings>(projects, state, resolver)
{
    public override async Task<int> ExecuteAsync(CommandContext context, StatusSettings settings, CancellationToken ct)
    {
        var (binding, env) = await ResolveAsync(settings, ct);
        var summary = await sync.StatusAsync(binding, env, ct);
        var report = new StatusReport(
            Path.GetFileNameWithoutExtension(binding.ValuesTarget.ProjectPath),
            binding.Service,
            binding.ValuesTarget,
            binding.SecretsTarget,
            binding.ActiveEnvironment,
            summary);

        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(report));
            return 0;
        }

        StatusView.Render(console, report);
        return 0;
    }
}
