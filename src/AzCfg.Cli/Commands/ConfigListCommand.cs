using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// <c>azcfg config list</c>: shows every per-user global setting (ADR 0008), then, inside a bound project (<c>--project</c>, default the
/// current directory), its <c>project.*</c> settings (ADR 0014).
/// </summary>
public sealed class ConfigListCommand(IUserSettingsService userSettings, IProjectSettingsService projectSettings, IAnsiConsole console)
    : AsyncCommand<ConfigListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ConfigListSettings settings, CancellationToken cancellationToken)
    {
        var list = (await userSettings.ListAsync(cancellationToken))
            .Concat(await projectSettings.ListAsync(Path.GetFullPath(settings.Project), cancellationToken))
            .ToList();
        if (settings.Json)
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(list));
        else
            console.Write(UserSettingsView.Table(list));
        return 0;
    }
}
