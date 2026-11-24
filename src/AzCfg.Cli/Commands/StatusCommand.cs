using AzCfg.Core.Abstractions;
using AzCfg.Core.Health;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Shows where an environment (<c>--env</c>, else the active one, else <c>dev</c>) stands against the local files: local changes,
/// remote changes, conflicts, blocked keys, warnings and the ignored count. Keys and counters only, never values. Needs a bound project; always exits 0.
/// With <c>--check</c> it reports configuration health instead (<see cref="IHealthService"/>) and exits 1 when there are errors.
/// </summary>
public sealed class StatusCommand(IProjectService projects, ISyncService sync, IUserStateStore state, IEnvironmentResolver resolver, IHealthService health, IAnsiConsole console)
    : EnvironmentCommandBase<StatusSettings>(projects, state, resolver, console)
{
    public override async Task<int> ExecuteAsync(CommandContext context, StatusSettings settings, CancellationToken ct)
    {
        if (settings.Check)
        {
            return await CheckAsync(settings, ct);
        }

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
            Console.Profile.Out.Writer.WriteLine(CliJson.Serialize(report));
            return 0;
        }

        StatusView.Render(Console, report);
        return 0;
    }

    /// <summary><c>--check</c>: configuration health (ADR 0013 amendment); ignores <c>--env</c>, exits 1 when the report has errors.</summary>
    private async Task<int> CheckAsync(StatusSettings settings, CancellationToken ct)
    {
        var report = await health.CheckAsync(Path.GetFullPath(settings.Project), ct);
        if (settings.Json)
        {
            Console.Profile.Out.Writer.WriteLine(CliJson.Serialize(report));
        }
        else
        {
            HealthView.Render(Console, report);
        }

        return report.HasErrors ? 1 : 0;
    }
}
