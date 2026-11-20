using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary><c>azcfg config get &lt;KEY&gt;</c>: shows one per-user global setting (ADR 0008) or, for a <c>project.*</c> key, a project setting (ADR 0014).</summary>
public sealed class ConfigGetCommand(IUserSettingsService userSettings, IProjectSettingsService projectSettings, IAnsiConsole console)
    : AsyncCommand<ConfigGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ConfigGetSettings settings, CancellationToken cancellationToken)
    {
        var setting = ProjectSettingKeys.IsProjectKey(settings.Key)
            ? await projectSettings.GetAsync(Path.GetFullPath(settings.Project), settings.Key, cancellationToken)
            : await userSettings.GetAsync(settings.Key, cancellationToken);
        if (settings.Json)
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(setting));
        else
            console.Write(UserSettingsView.Table([setting]));
        return 0;
    }
}
