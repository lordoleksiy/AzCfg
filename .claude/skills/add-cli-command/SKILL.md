---
name: add-cli-command
description: Add a new azcfg CLI command (Spectre.Console.Cli) with human output and a --json mode. Use when asked to add or extend a CLI command.
---

# Add a CLI command

The CLI only talks to the user; logic lives in Core use cases (`SetupService`, `ProjectService`, `SyncService`). Core never prompts or prints.

Files to touch:
1. `src/AzCfg.Cli/Commands/<Name>Command.cs` — `AsyncCommand<<Name>Settings>`; settings derive from the shared base with global options `--env`, `--json`, `--yes`, `--project`.
2. `src/AzCfg.Cli/Commands/<Name>Settings.cs` — one type per file.
3. `src/AzCfg.Cli/Program.cs` — register with `config.AddCommand<<Name>Command>("<name>")`.
4. If new behavior is needed, add it to the Core use case (with tests in `tests/AzCfg.Core.Tests`), not in the command.

Output:
- Default: Spectre.Console tables/prompts (prompts only when not `--yes` and not `--json`).
- `--json`: serialize the use-case result record as-is (`System.Text.Json`, camelCase) to stdout; no prompts, no decorative text; errors as JSON with non-zero exit code.

Finish with `dotnet build` and `dotnet test`.
