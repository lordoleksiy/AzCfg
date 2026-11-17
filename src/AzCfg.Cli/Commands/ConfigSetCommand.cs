using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary><c>azcfg config set &lt;KEY&gt; &lt;VALUE&gt;</c>: validates and saves one per-user global setting (ADR 0008).</summary>
public sealed class ConfigSetCommand(IUserSettingsService userSettings, IAnsiConsole console) : AsyncCommand<ConfigSetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ConfigSetSettings settings, CancellationToken cancellationToken)
    {
        var setting = await userSettings.SetAsync(settings.Key, settings.Value, cancellationToken);
        if (settings.Json)
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(setting));
        else
            console.WriteLine($"{setting.Key} = {setting.Value ?? UserSettingsView.None}");
        return 0;
    }
}
