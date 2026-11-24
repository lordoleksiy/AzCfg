using AzCfg.Core.UseCases;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Renders user settings for <c>azcfg config</c>: a Key/Value table, unset values as <c>(none)</c>; a value kept in a shared
/// manifest is followed by its path, e.g. <c>project.shared = true (C:\repo\azcfg.json)</c>.
/// </summary>
public static class UserSettingsView
{
    public const string None = "(none)";

    public static Table Table(IEnumerable<UserSetting> settings)
    {
        var table = new Table().AddColumns("Key", "Value");
        foreach (var s in settings)
            table.AddRow(Markup.Escape(s.Key), Markup.Escape(s.Source is null ? s.Value ?? None : $"{s.Value ?? None} ({s.Source})"));
        return table;
    }
}
