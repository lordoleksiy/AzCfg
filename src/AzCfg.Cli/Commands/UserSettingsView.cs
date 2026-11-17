using AzCfg.Core.UseCases;
using Spectre.Console;

namespace AzCfg.Cli.Commands;

/// <summary>Renders user settings for <c>azcfg config</c>: a Key/Value table, unset values as <c>(none)</c>.</summary>
public static class UserSettingsView
{
    public const string None = "(none)";

    public static Table Table(IEnumerable<UserSetting> settings)
    {
        var table = new Table().AddColumns("Key", "Value");
        foreach (var s in settings)
            table.AddRow(Markup.Escape(s.Key), Markup.Escape(s.Value ?? None));
        return table;
    }
}
