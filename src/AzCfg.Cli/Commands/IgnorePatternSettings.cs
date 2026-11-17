using System.ComponentModel;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Settings of <c>ignore add</c> and <c>ignore remove</c>: the global options plus the pattern.</summary>
public sealed class IgnorePatternSettings : GlobalSettings
{
    [CommandArgument(0, "<PATTERN>")]
    [Description("Key pattern: optional 'global:' prefix, then a key path with '*' as wildcard (e.g. 'Logging:*', 'global:*', '*:Password').")]
    public string Pattern { get; init; } = "";
}
