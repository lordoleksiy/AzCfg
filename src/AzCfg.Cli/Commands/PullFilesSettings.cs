using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Settings of <c>pull</c>: the pull options plus the key selection (ADR 0013).</summary>
public sealed class PullFilesSettings : PullSettings
{
    [CommandOption("--key <PATTERN>")]
    [Description("Pull only the keys matching the pattern (repeatable; * wildcard, optional global: prefix). Active environment only; --force then overwrites a changed local key (keys never pushed are kept).")]
    public string[] Key { get; init; } = [];

    public override ValidationResult Validate() => KeyFilter.Validate(Key);
}
