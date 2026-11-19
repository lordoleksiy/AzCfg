using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Settings of <c>diff</c>: up to two sides (an environment name or <c>local</c>) plus <c>--same</c>, <c>--missing</c> and <c>--all</c>.</summary>
public sealed class DiffSettings : GlobalSettings
{
    [CommandArgument(0, "[left]")]
    [Description("First side: an environment name or 'local'. Alone, it is compared with the local files; omit both to compare local with the active environment.")]
    public string? Left { get; init; }

    [CommandArgument(1, "[right]")]
    [Description("Second side: an environment name or 'local'.")]
    public string? Right { get; init; }

    [CommandOption("--same")]
    [Description("Also show keys that are equal on both sides.")]
    public bool Same { get; init; }

    [CommandOption("--missing")]
    [Description("List keys that exist in some environments but not in others (requires --all).")]
    public bool Missing { get; init; }

    [CommandOption("--all")]
    [Description("With --missing: check every configured environment. Exit code 1 when a key is missing.")]
    public bool All { get; init; }

    public override ValidationResult Validate()
    {
        if (Missing != All)
        {
            return ValidationResult.Error("--missing and --all go together: use 'azcfg diff --missing --all'.");
        }

        return Missing && (Left is not null || Right is not null)
            ? ValidationResult.Error("--missing --all checks every environment and takes no sides.")
            : ValidationResult.Success();
    }
}
