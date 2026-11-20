# CLI roadmap analysis: daily workflow, consistency, environment switching

Status: **accepted** (2026-11-20). Contract: ADR 0013 (+ amendment of ADR 0012). Tasks: `docs/mvp-plan.md`
stages 8–10; UI side: task M1 in `docs/ui-mvp-plan.md`. Releases go to `master` **before** the UI. Sources: `docs/architecture.md` §«Майбутнє», `docs/mvp-plan.md`
(stage 8, «Поза MVP»), ADR 0010–0012, `docs/ui-mvp-plan.md` (U1–U4 open), the code of `AzCfg.Core`, `AzCfg.Cli`,
`AzCfg.Ui.Application`. Purpose: one place to restore the context of «what to build next in the CLI and why».

## 1. Constraint: do not overload the CLI
- Today: 9 top-level commands (`init`, `config`, `pull`, `use`, `plan`, `push`, `edit`, `promote`, `ignore`).
- Taking the roadmap literally would add 7+ (`adopt`, `standardize`, `validate`, `diff`, `doctor`, plus the obvious
  `get/set/unset`, `resolve`) → ~17 commands. Too many for a tool used a few times a day.
- Rules for this plan:
  1. **At most +2–3 top-level commands in total.** Prefer a flag on an existing command over a new command.
  2. **Every new capability is a Core use case shared with the UI.** The CLI is a thin view over it (ADR 0003);
     no logic exists only in the CLI or only in `AzCfg.Ui.Application`.
  3. Existing commands keep their behaviour and names (no breaking renames, e.g. `plan` stays).

## 2. Findings (facts from the code)
**Core already has, the CLI does not expose** (built in C1/C2 for the UI):
`EnvironmentComparer` (env ↔ env), `ISyncService.DiffAsync` (local/remote/base), `PullSelectedAsync`,
`PlanSelection.Select`, `ResolveConflictAsync`, `EditJsonPatch` (set/remove one key), `EditedJson.BaseJson`,
`ReadRemoteConfigAsync`. → Most of the useful CLI additions are cheap: wiring + views + tests.

**Logic that lives in `AzCfg.Ui.Application` but belongs in Core** (the CLI cannot reuse it there):
- `CompareService` local ↔ remote by fingerprint (FR-C4) — needed by `diff`.
- `SyncWorkflowService.UnmanagedLocalKeysAsync` (G20) and `PreviewPullAsync` — needed by `use`/`pull`.
- `AzureStatusService.CheckEnvironmentAsync` (access probe) — needed by a health check.
- `EnvironmentRules.SelectDefault/KindOf` (G18 was moved *out* of the CLI into the UI layer).
- The FR-S7 sync summary (⇡ local, ⇣ remote, ! conflicts) — needed by `status`.

**CLI gaps / risks in the current commands:**
- `use <env>` on a project without an active env silently overwrites existing local files (G20 fixed only in the UI plan).
- `use prod` writes production secrets into local files without any warning (UI warns, FR-S6).
- `edit` pushes `EditedJson` without `BaseJson` → an edit kept open long reverts others' changes (G14, fixed only for UI drafts).
- A blocked pull says only «re-run with --force», without listing which keys would be lost.
- No way to see «where am I» (active env, local ⇡, remote ⇣, conflicts) without running `plan` (push direction only).
- Changing one key needs either an editor (`edit`) or editing a local file + `push`.
- Consistency between environments: `promote` only adds missing service keys; there is no way to see value drift or keys
  missing in some environments.

## 3. Evaluation
Scale 1–3 (3 = high). «Cost» 3 = cheap. UI = how much it helps the UI work (U1–U4).

| Item | Need | Use | Cost | UI | Verdict |
| --- | --- | --- | --- | --- | --- |
| `status` (active env, ⇡/⇣/conflicts, blocked keys) | 3 | 3 | 3 | 3 (FR-S7, header) | **Do, new command** |
| `diff [a] [b]` (local↔env, env↔env, `--missing` across envs) | 3 | 3 | 2 | 3 (U3 Compare) | **Do, new command** (from «Майбутнє») |
| `pull/plan/push --key <pattern>` | 2 | 3 | 3 | 2 (same selection as U4) | **Do, flag** |
| Conflict resolution: `pull --key K --force` / `push --key K --force` | 2 | 2 | 3 | 2 | **Do, flag** (no `resolve` command) |
| `edit --set K=V / --unset K` (no editor) + `BaseJson` | 2 | 3 | 3 | 2 (same draft patch as U2) | **Do, flag** (no `get/set/unset` commands) |
| `use` safety: G20 confirm, protected-env confirm, list blocked keys | 3 | 2 | 3 | 3 (U4 ActivationFlow) | **Do, behaviour** |
| `promote --key`, `--changed` (also update differing plain values) | 2 | 2 | 2 | 1 (FR-C5 copy) | Do later, flag |
| `doctor` (sign-in, access, `.gitignore`, dead KV refs) | 2 | 2 | 2 | 3 (FR-E6 env health) | **Do as `status --check`**, not a command |
| `adopt` (ADR 0012) | 2 | 2 | 1 | 1 | Keep, after phase A |
| `validate` (ADR 0012) | 1 | 2 | 2 | 1 | Fold into `status --check` (exit code ≠ 0) |
| `standardize` + convention profiles (ADR 0012) | 1 | 1 | 1 | 1 | **Defer** (rare, risky, large) |
| `LocalSettingsTarget` | 2 (Functions) | 2 | 2 | 1 (G17) | Later, no new command |
| Vault ranking from Resource Graph | 1 | 2 | 2 | 1 (G16) | Later |
| Key Vault creation, CI/CD generation, manifest-driven sync | 1 | 1 | 1 | 0 | Out of scope for now |

Command budget: today 9 → after this plan **11** (`status`, `diff`) → **12** with `adopt`.
The roadmap as written would give ~17.

## 4. Plan
### Phase A — daily workflow (CLI 0.4.0). Do before UI U2–U4.
- **A1. Core: lift the shared logic out of `AzCfg.Ui.Application`** (TDD, additive):
  - `SyncStatus` use case: `ISyncService.StatusAsync(binding, env)` → `SyncSummary(ActiveEnv, LocalChanges, RemoteChanges,
    Conflicts, BlockedKeys, Warnings)` built from `DiffAsync` (value-free).
  - `ConfigComparison` use case: local↔remote by fingerprint + remote↔remote via `EnvironmentComparer`
    (moved from `CompareService`; the UI service becomes a JSON/row adapter over it).
  - `UnmanagedLocalKeysAsync` (G20) and `EnvironmentRules` → Core; UI.Application delegates.
  - `PushSource.EditedJson` from the CLI always carries `BaseJson`.
- **A2. `azcfg status`** — one screen: project, service, active env, ⇡/⇣/!, blocked keys, warnings; `--env` for another
  env (remote-only part); `--json` = `SyncSummary`.
- **A3. `azcfg diff [<left>] [<right>]`** — no args: local ↔ active; one arg: local ↔ env; two: env ↔ env.
  `--all` (only with `--missing`): keys present in some environments but missing in others (consistency check, exit
  code ≠ 0 when found → usable in CI). Secrets compared by fingerprint / secret name, never by value.
- **A4. Flags on existing commands:**
  - `pull --key`, `plan --key`, `push --key` (repeatable, `*` patterns as in ignore) → `PullSelectedAsync`, `PlanSelection`.
  - Conflicts: `pull --key K --force` = keep remote; `push --key K --force` = keep local (`ResolveConflictAsync` + push).
  - `edit --set K=V`, `--unset K`, `--global`, `--secret K=name` → `EditJsonPatch` over the export, then the usual plan/apply;
    no editor opened.
- **A5. `use` safety:** list blocked keys with the hint `push` / `--force`; first activation with existing local keys →
  confirmation (G20); protected env → «its secrets will be written locally» confirmation (`--yes`/`--confirm` in scripts).
- **A6. Release 0.4.0:** README, CHANGELOG, version.

### Phase B — health (CLI 0.5.0)
- **B1. Core `HealthReport`** (sign-in, per-env access probe moved from `AzureStatusService`, targets git-ignored,
  dead KV references, keys missing across envs from A3, manifest validation once it exists). Dead-reference check needs
  a value-free port method (e.g. `ISecretStore.ExistsAsync`) — never read a secret value for it.
- **B2. `azcfg status --check`** (= the planned `doctor` + `validate`), exit code ≠ 0 on errors.
  The UI's environment health (FR-E6) and a future «Problems» panel read the same report.

### Phase C — project configuration (ADR 0012, reduced)
- Keep PC1, PC3–PC6: manifest model/store, discovery, `azcfg adopt`.
- PC7 `validate` → part of `status --check` (B2), no separate command.
- PC2 convention profiles + PC8 `standardize` → deferred until there is real demand (teams with many services).
- Needs an amendment of ADR 0012 and of «Етап 8 — пріоритет над подальшим UI» in `architecture.md`/`mvp-plan.md`.

### Later (no new commands)
`promote --key/--changed`, `LocalSettingsTarget`, vault ranking, Key Vault creation, manifest-driven pull/push, CI/CD.

## 5. Effect on the UI work
| UI task | Gets from this plan |
| --- | --- |
| U1 env/settings | `EnvironmentRules` in Core; env health from `HealthReport` (B1) |
| U2 Browse / header | `SyncSummary` for FR-S7 and the project header; `edit --set` uses the same `EditJsonPatch` as drafts |
| U3 Compare | `ConfigComparison` in Core, already tested through `diff`; `--missing` matrix as a possible extra view |
| U4 Sync / make active | G20 + protected warning + conflict resolution in Core, already exercised by `use`/`pull`/`push` |

Ordering: **A1 before U2–U4**, so the UI is built on Core use cases instead of growing more logic in
`AzCfg.Ui.Application`. A2–A5 can run in parallel with U1. The CLI then works as a fast, scriptable test harness for
every behaviour the UI shows (`--json` = the same records).

## 6. Decisions (taken 2026-11-20)
1. Command budget accepted: +`status`, +`diff`, later +`adopt`; no `doctor`/`validate`/`standardize`/`get`/`set` commands.
2. Order: 0.4.0 (phase A) → 0.5.0 (phase B) → 0.6.0 (phase C, ADR 0012 reduced); all before the UI reaches `master`.
3. Convention profiles and `standardize` deferred (ADR 0012 amendment).
4. Flags: `--key` (selection), `--force` with `--key` (per-key conflict resolution), `edit --set/--unset/--secret`.
5. UI distribution: inside the `azcfg` tool as `azcfg ui` (a launcher, +1 top-level command on top of the budget),
   not a separate package. The UI reaches `master` as CLI 0.7.0 (UI preview) after 0.6.0 (UI tasks X1, R1).

## 7. Execution (summary; tasks in `docs/mvp-plan.md`)
- 0.4.0 is released (`master`, PR #53). `dev` still holds the unfinished UI and lacks 0.3.1/0.4.0 until M1.
- Each release: `release/<ver>` from `master` → task branches/PRs into it → one PR into `master` (every push to `master`
  touching `src/**` publishes NuGet, so no intermediate PRs into `master`) → merge `master` into `dev`.
- 0.4.0: R0 docs → CW1 cherry-pick C1/C2 (`e2b3983`, `3ef4bfe`) → CW2–CW4 Core (parallel) → CW5–CW9 CLI (parallel by
  file) → CW10 release. Then UI M1: merge into `dev`, Application delegates to Core; U2–U4 start after M1.
- 0.5.0: H0 PR CI + `--skip-duplicate` → H1 `HealthReport` (+ value-free `ISecretStore.ExistsAsync`, ADR note first)
  → H2 `status --check`; H4 editable project settings `config project.*` (ADR 0014, parallel) → H3 release → UI M2.
- 0.6.0: PR0 remote providers (ADR 0014, no user-visible change) → PC1, PC3–PC7 (adopt, manifest checks in
  `status --check`) → PC9 release → UI M3.
- UI on `dev` does not wait for 0.5.0/0.6.0 (they only gate the UI reaching `master`): M1 → U2–U4 in parallel with
  0.5.0; U1 after M2 (env health from `HealthReport`, project settings from `IProjectSettingsService`); stage 10 runs in
  parallel with the UI.
