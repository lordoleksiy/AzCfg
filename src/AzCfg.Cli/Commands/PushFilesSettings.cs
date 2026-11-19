using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Settings of <c>push</c>: the push options plus the push mode for local files (ADR 0008) and the key selection (ADR 0013).</summary>
public sealed class PushFilesSettings : PushSettings
{
    [CommandOption("--delete")]
    [Description("Also delete remote keys that were deleted locally (default: add and update only).")]
    public bool Delete { get; init; }

    [CommandOption("--key <PATTERN>")]
    [Description("Push only the keys matching the pattern (repeatable; * wildcard, optional global: prefix).")]
    public string[] Key { get; init; } = [];

    [CommandOption("--force")]
    [Description("With --key: keep the local value of selected keys that also changed remotely (active environment only).")]
    public bool Force { get; init; }

    public override ValidationResult Validate() =>
        Force && Key.Length == 0 ? ValidationResult.Error("--force needs --key (it resolves conflicts of the selected keys).") : ValidationResult.Success();
}
