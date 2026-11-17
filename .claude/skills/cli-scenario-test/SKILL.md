---
name: cli-scenario-test
description: Write an end-to-end azcfg CLI scenario test - the real command tree and real Core services over in-memory Azure/file fakes (labels, etags, secrets, user state). Use to verify a user workflow (init → pull → ignore → plan/push) without Azure, or when a change spans Core + CLI.
argument-hint: <workflow to cover>
---

# CLI scenario test

Unit tests stub one layer each; a scenario test runs `azcfg` commands exactly as a user types them, through
the production command tree (`CliApp.Configure`) and the real Core services, with only the edges faked.

## Harness (tests/AzCfg.Cli.Tests/Scenario/, one type per file)
- `ScenarioHost` — builds the `ServiceCollection` like `AddAzCfg()` but with fakes for the edges:
  `AppConfig` (`InMemoryAppConfig : IConfigStore`), `Secrets` (`InMemorySecretStore : ISecretStore`),
  `State` (`InMemoryUserStateStore : IUserStateStore`), `Files` (`InMemoryLocalTarget : ILocalTarget`, UserSecrets, safe for
  secrets), `Detector` (`FixedProjectDetector`: one project `Orders` at `<Repo>/Orders/Orders.csproj`, `Repo` is a temp-like
  path never touched on disk), `Discovery` (`FixedResourceDiscovery`: `AddStore(name, endpoint)`, `AddVault`), `NoOpEditorLauncher`.
  Real: `SetupService`, `ProjectService`, `SyncService`, `UserSettingsService`, `ProfileEnvironmentResolver`,
  `HmacSecretFingerprinter` (key from the state store). Commands = `CliApp.Configure(c)` + `PropagateExceptions()` +
  `ConfigureConsole(...)`; console = `AnsiConsole.Create` over a `StringWriter` (no ANSI, no colors, non-interactive, no default
  enrichers, width 200). `Task<(int Exit, string Output)> RunAsync(params string[] args)` appends `--project <Repo>`;
  exceptions propagate (assert them). `host.Binding` = the saved binding of `Repo`.
- `InMemoryAppConfig` mirrors App Configuration REST semantics — keep it faithful, scenarios depend on it:
  - settings keyed by `(store, key, label)`; `null` label = no label; each write gets a new etag;
  - read selector: `env.Label == null` → all labels; otherwise unlabeled + `env.Label` (what `AppConfigStore` queries),
    `global:` then `<service>:`, each ordered by key then label (unlabeled first); `ListServicePrefixesAsync` uses the same labels;
  - `ConfigEntry.Label` = the setting's label; Add fails if `(key,label)` exists; Update/Delete of a missing setting or with a
    stale etag → key in `ApplyResult.Failed`; Key Vault references are `ConfigValue.KeyVaultRef`;
  - seed: `Set(endpoint, "orders:Db:Host", "x", label: "Development")`, `SetSecretReference(endpoint, key, secretUri, label)`
    (both return the `RemoteSetting`); inspect: `Get(endpoint, key, label)`, `Copies(endpoint, key)`, `Settings(endpoint)`,
    `Writes` (successful applies). `RemoteSetting` is a record (key, label, value, etag; `Text`), so `Assert.Equal(seeded, Get(...))`
    proves a setting was not rewritten.
- `InMemorySecretStore`: `Add(vault, name, value)` returns the secret URI to reference.
- `InMemoryUserStateStore` stores everything as JSON (same options as `JsonUserStateStore`), so state must round-trip;
  `Profile`, `Binding(repo)`, `Snapshot(repo, env)`, `SnapshotJson(repo, env)` (assert secrets never land there).
- Local files: `InMemoryLocalTarget` keeps `LocalValue`s per target and validates writes as nested JSON; helpers on the single
  file: `Values()`, `Local(key)`, `SetLocal`, `RemoveLocal`, `Writes`.
- Output helpers: `ScenarioJson` (`Parse`, `Warnings` → `(kind, key)`, `Changes` → `(kind, scope, key, label, beforeLabel)`,
  `Strings`, `AssertApplied(output, n)`), `ScenarioOutput.AssertLine(output, parts...)` for human tables/warnings.

## Writing a scenario
1. One test class per workflow/mode (`StoreModeScenarioTests`, `LabelModeScenarioTests`, `ServiceBindingScenarioTests`,
   `UpsertScenarioTests`, …), Arrange =
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
