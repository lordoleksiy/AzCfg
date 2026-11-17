using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary><c>azcfg config list</c>: shows every per-user global setting (ADR 0008).</summary>
public sealed class ConfigListCommand(IUserSettingsService userSettings, IAnsiConsole console) : AsyncCommand<ConfigListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ConfigListSettings settings, CancellationToken cancellationToken)
    {
        var list = await userSettings.ListAsync(cancellationToken);
        if (settings.Json)
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(list));
        else
            console.Write(UserSettingsView.Table(list));
        return 0;
    }
}
