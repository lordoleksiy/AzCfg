---
name: add-cli-command
description: Add a new azcfg CLI command (Spectre.Console.Cli) with human output and a --json mode. Use when asked to add or extend a CLI command.
---

# Add a CLI command

The CLI only talks to the user; logic lives in Core use cases. Core never prompts or prints.

Files to touch:
1. `src/AzCfg.Cli/Commands/<Name>Command.cs` — `AsyncCommand<<Name>Settings>`; settings derive from `GlobalSettings` (options `--env`, `--json`, `--yes`, `--project`). Commands that resolve the binding/environment reuse `EnvironmentCommandBase`, `PullCommandBase` or `PushCommandBase`; warnings go through `WarningsView`.
2. `src/AzCfg.Cli/Commands/<Name>Settings.cs` — one type per file.
3. `src/AzCfg.Cli/CliApp.cs` (`CliApp.Configure`) — register with `config.AddCommand<<Name>Command>("<name>")`, or `config.AddBranch(...)` for a command group (`ignore`, `config`). `Program.cs` only runs `CliApp`.
4. If new behavior is needed, add it to the Core use case (`SetupService`, `ProjectService`, `SyncService`, `UserSettingsService`; tests in `tests/AzCfg.Core.Tests`), not in the command.
5. Tests: `tests/AzCfg.Cli.Tests/` (human output + `--json`); for a user workflow spanning commands add a scenario with `/cli-scenario-test`.
6. Document the command in `README.md` → Usage.

Output:
- Default: Spectre.Console tables/prompts (prompts only when not `--yes` and not `--json`).
- `--json`: serialize the use-case result record as-is (`System.Text.Json`, camelCase) to stdout; no prompts, no decorative text; errors as JSON with non-zero exit code.

Finish with `dotnet build` and `dotnet test`.
