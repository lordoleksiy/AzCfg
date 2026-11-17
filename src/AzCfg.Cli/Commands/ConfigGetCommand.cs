using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary><c>azcfg config get &lt;KEY&gt;</c>: shows one per-user global setting (ADR 0008).</summary>
public sealed class ConfigGetCommand(IUserSettingsService userSettings, IAnsiConsole console) : AsyncCommand<ConfigGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ConfigGetSettings settings, CancellationToken cancellationToken)
    {
        var setting = await userSettings.GetAsync(settings.Key, cancellationToken);
        if (settings.Json)
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(setting));
        else
            console.Write(UserSettingsView.Table([setting]));
        return 0;
    }
}
