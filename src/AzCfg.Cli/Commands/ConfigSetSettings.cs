using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Options of <c>azcfg config set &lt;KEY&gt; &lt;VALUE&gt;</c>.</summary>
public sealed class ConfigSetSettings : ConfigGetSettings
{
    [CommandArgument(1, "<VALUE>")]
    [Description("New value: 'stores' or 'labels' for mode, a label (empty clears it), an https endpoint, true / false, a service name, <target>[:<project path>] (user-secrets, appsettings-local, local-settings) or a Key Vault URI (empty clears it).")]
    public string Value { get; init; } = "";
}
