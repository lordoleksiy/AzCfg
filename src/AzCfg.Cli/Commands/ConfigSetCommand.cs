using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// <c>azcfg config set &lt;KEY&gt; &lt;VALUE&gt;</c>: validates and saves one per-user global setting (ADR 0008) or, for a <c>project.*</c> key, a project
/// setting (ADR 0014). After a new service or local target the human output adds the follow-up command; <c>--json</c> stays the setting alone.
/// </summary>
public sealed class ConfigSetCommand(IUserSettingsService userSettings, IProjectSettingsService projectSettings, IAnsiConsole console)
    : AsyncCommand<ConfigSetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ConfigSetSettings settings, CancellationToken cancellationToken)
    {
        var setting = ProjectSettingKeys.IsProjectKey(settings.Key)
            ? await projectSettings.SetAsync(Path.GetFullPath(settings.Project), settings.Key, settings.Value, cancellationToken)
            : await userSettings.SetAsync(settings.Key, settings.Value, cancellationToken);
        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(setting));
            return 0;
        }

        console.WriteLine($"{setting.Key} = {setting.Value ?? UserSettingsView.None}");
        if (ProjectSettingKeys.Hint(settings.Key) is { } hint)
            console.WriteLine(hint);
        if (setting.Hint is { } shared)
            console.WriteLine(setting.Source is null ? shared : $"{shared} ({setting.Source})");
        return 0;
    }
}
