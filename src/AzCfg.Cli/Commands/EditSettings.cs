using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

public sealed class EditSettings : PushSettings
{
    [CommandOption("--set <KEY=VALUE>")]
    [Description("Set a plain value without opening the editor (repeatable). Not for secrets.")]
    public string[] Set { get; init; } = [];

    [CommandOption("--unset <KEY>")]
    [Description("Remove a key without opening the editor (repeatable).")]
    public string[] Unset { get; init; } = [];

    [CommandOption("--secret <KEY=SECRET-NAME>")]
    [Description("Point a key at an existing Key Vault secret by name (repeatable). Secret values are never accepted here.")]
    public string[] Secret { get; init; } = [];

    [CommandOption("--global")]
    [Description("With --set/--unset/--secret: put the keys in the global scope (shared by every service).")]
    public bool Global { get; init; }

    /// <summary><c>true</c> when any of <c>--set</c>, <c>--unset</c>, <c>--secret</c> is given (no editor is started).</summary>
    public bool HasPatch => Set.Length + Unset.Length + Secret.Length > 0;

    public override ValidationResult Validate()
    {
        if (Global && !HasPatch)
        {
            return ValidationResult.Error("--global needs at least one of --set, --unset, --secret.");
        }

        foreach (var (option, values) in new[] { ("--set", Set), ("--secret", Secret) })
        {
            foreach (var value in values)
            {
                var i = value.IndexOf('=', StringComparison.Ordinal);
                if (i <= 0 || (option == "--secret" && i == value.Length - 1))
                {
                    return ValidationResult.Error($"Invalid {option} '{value}': expected {(option == "--set" ? "KEY=VALUE" : "KEY=SECRET-NAME")}.");
                }
            }
        }

        return Unset.Any(string.IsNullOrWhiteSpace) ? ValidationResult.Error("Invalid --unset: key must not be empty.") : ValidationResult.Success();
    }
}
