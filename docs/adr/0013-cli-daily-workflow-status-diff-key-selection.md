# 0013. CLI daily workflow: `status`, `diff`, key selection, safe `use`

Status: accepted (CLI 0.4.0, stage 8 of `docs/mvp-plan.md`; ships to `master` **before** the UI). Analysis behind it:
`docs/cli-roadmap.md`.

## Context
- The everyday loop is «switch env → see what differs → change one or two keys → push/pull them». Today the CLI covers it
  only coarsely: no «where am I» view, no env ↔ env comparison, pull/push are all-or-nothing, changing one key needs an
  editor or a local edit + push.
- Core already has the building blocks (added for the UI in C1/C2 on `dev`): `EnvironmentComparer`, `PlanSelection`,
  `EditJsonPatch`, `PullSelectedAsync`, `ResolveConflictAsync`, `ReadRemoteConfigAsync`, `EditedJson.BaseJson`,
  `ApplyGuard`. They are not on `master` yet and the CLI does not use them.
- Some behaviour the CLI also needs was written in `AzCfg.Ui.Application`: local ↔ remote comparison by fingerprint,
  the first-pull overwrite check (G20), the sync summary (FR-S7), environment rules (G18). The CLI cannot reuse it there.
- Known CLI risks: first `use` silently overwrites existing local files; `use prod` writes production secrets locally
  without a warning; `edit` pushes without `BaseJson` (G14); a blocked pull does not say which keys block it.
- The owner does not want the CLI overloaded with commands.

## Decision
### Command budget
- Two new top-level commands: `status`, `diff`. Everything else is a flag or a behaviour of an existing command.
- Rejected as separate commands: `get`/`set`/`unset`, `resolve`, `doctor`, `validate` (see ADR 0012 amendment).
- Existing command names and default behaviour do not change.

### Core (additive, TDD)
- Port C1/C2 to `master` (same code and tests as on `dev`).
- `ISyncService.StatusAsync(binding, env, ct) → SyncSummary(string Environment, bool IsActive, int LocalChanges,
  int RemoteChanges, IReadOnlyList<string> Conflicts, IReadOnlyList<string> BlockedKeys, IReadOnlyList<SyncWarning> Warnings,
  int IgnoredCount)` — built from `DiffAsync`; keys only, never values. For a non-active env: remote-only counts are
  not computed (`IsActive = false`, counts 0).
- `IConfigComparisonService.CompareAsync(binding, CompareSide left, CompareSide right, ct) → ConfigComparison(Items,
  IgnoredCount)`; `CompareSide` = `Local` | `Environment(ConfigEnvironment)`. Local ↔ env: `DiffAsync` local vs remote,
  secrets equal by `ISecretFingerprinter`; env ↔ env: `ReadRemoteConfigAsync` ×2 + `EnvironmentComparer` (secrets by
  name). Local ↔ Local → `ArgumentException`. Items carry `ComparisonKind`, values only for plain keys; secrets never.
- `MissingKeysReport` (static, pure): N effective configurations → keys present in some environments and missing in
  others (`Key`, `PresentIn`, `MissingIn`).
- `ISyncService.UnmanagedLocalKeysAsync(binding, ct)` (G20): local keys when `ActiveEnvironment` is null, else empty.
- `EnvironmentRules` (`IsActive`, `SelectDefault`, `KindOf`) in `Core/Environments`.
- `KeySelector` (static): matches keys against repeatable `--key` patterns with the `IgnoreRules` pattern syntax
  (`*`, optional `global:`).

### CLI
- `azcfg status [--env <ENV>]` — project, service, targets, active env, ⇡ local / ⇣ remote / ! conflicts, blocked keys,
  warnings, ignored count. `--json` = `SyncSummary` + binding info. Exit code 0.
- `azcfg diff [<left>] [<right>] [--missing] [--all]`:
  no args → local ↔ active; `<env>` → local ↔ env; `<a> <b>` → env ↔ env (sides may be `local`).
  Differences only by default (`--same` shows equal keys). `--missing --all` → `MissingKeysReport` over every configured
  environment, exit code 1 when anything is missing (usable in CI). `--json` = the record as-is.
- `--key <pattern>` (repeatable) on `pull`, `plan`, `push`: `pull` → `PullSelectedAsync` (active env only, as in AD-8);
  `plan`/`push` → `PlanSelection.Select`.
- Conflict resolution per key: `pull --key K --force` = keep remote; `push --key K --force` = keep local
  (planned in memory with `PushSource.LocalFiles(KeepLocal: keys)`; the snapshot changes only when the plan is applied,
  so a declined or failed push keeps the conflict). `--force` without `--key` keeps its current meaning. `pull --key`
  leaves keys added locally and never pushed (nothing to pull; `--force` does not delete them).
- `edit --set K=V`, `--unset K`, `--secret K=<secret-name>` (repeatable; `--global` puts them under `@global`) — patch the
  export with `EditJsonPatch`, no editor; then the usual plan → confirm → apply. Secret *values* are not accepted on the
  command line (shell history); a new secret value still goes through the editor or the UI.
- `edit` (both modes) always sends `EditedJson(json, BaseJson: export)`.
- `use <env>`: blocked → list blocked keys + hint (`push`, `pull --key … --force`, `--force`); first activation with
  unmanaged local keys → confirmation; protected env → confirmation «secrets of <env> will be written to local files».
  `--yes` skips the first, `--confirm <ENV>` the second. Active env unreadable (Azure error, lost access) → warning +
  confirmation; `--json` → refusal `unverified`; `--force` skips the check.

## Consequences
- CLI 0.4.0 ships from `master` before the UI; `master` is merged back into `dev`, after which `AzCfg.Ui.Application`
  delegates to the new Core services (UI task M1 in `docs/ui-mvp-plan.md`) instead of keeping its own copies.
- ADR 0012 moves to later releases (0.5.0 health, 0.6.0 project configuration) and is reduced (see its amendment).
- No new packages, no new ports.
