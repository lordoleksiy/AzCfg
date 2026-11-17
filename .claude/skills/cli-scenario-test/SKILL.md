---
name: cli-scenario-test
description: Write an end-to-end azcfg CLI scenario test - the real command tree and real Core services over in-memory Azure/file fakes (labels, etags, secrets, user state). Use to verify a user workflow (init → pull → ignore → plan/push) without Azure, or when a change spans Core + CLI.
argument-hint: <workflow to cover>
---

# CLI scenario test

Unit tests stub one layer each; a scenario test runs `azcfg` commands exactly as a user types them, through
the production command tree (`CliApp.Configure`) and the real Core services, with only the edges faked.

## Harness (tests/AzCfg.Cli.Tests/Scenario/)
- `ScenarioHost` — builds the `ServiceCollection` like `AddAzCfg()` but with fakes for the edges:
  `InMemoryAppConfig : IConfigStore`, `InMemorySecretStore : ISecretStore`, `InMemoryUserStateStore : IUserStateStore`,
  `InMemoryLocalTarget : ILocalTarget` (UserSecrets, safe for secrets), a fixed `IProjectDetector` and
  `IResourceDiscovery`. Real: `SetupService`, `ProjectService`, `SyncService`, `ProfileEnvironmentResolver`,
  `HmacSecretFingerprinter`, any settings service. Console = `AnsiConsole.Create` over a `StringWriter`.
  `Task<(int Exit, string Output)> RunAsync(params string[] args)`; exceptions propagate (assert them).
- `InMemoryAppConfig` mirrors App Configuration REST semantics — keep it faithful, scenarios depend on it:
  - settings keyed by `(key, label)`; `null` label = no label; each write bumps an etag;
  - read selector: `env.Label == null` → all labels; otherwise unlabeled + `env.Label` (what `AppConfigStore` queries);
  - `ConfigEntry.Label` = the setting's label; Add fails if `(key,label)` exists; Update/Delete with a stale
    etag → key in `ApplyResult.Failed`;
  - helpers to seed (`Set(endpoint, "orders:Db:Host", "x", label: "Development")`) and inspect.
- Local files: `InMemoryLocalTarget` keeps `LocalValue`s per target; helpers `Local(key)`, `SetLocal`, `RemoveLocal`.

## Writing a scenario
1. One test class per workflow/mode (`StoreModeScenarioTests`, `LabelModeScenarioTests`, …), Arrange =
   seed stores + project, then a linear story of commands, asserting after each step:
   exit code, `--json` output (`JsonDocument`), local values, remote settings (with labels), warnings.
2. Always cover the user-facing invariants:
   - env name ≠ label in store mode still pulls (`dev` + label `Development`);
   - default push never deletes; `--delete` does;
   - ignored keys survive pull and never reach push;
   - label mode never modifies another label's setting (override + warning);
   - secrets never appear in `--json` output.
3. Prefer `--json --yes` for steps whose output you parse; use human output only to check rendering.
4. Run: `dotnet test tests/AzCfg.Cli.Tests --filter "FullyQualifiedName~Scenario" -v q --nologo`.
