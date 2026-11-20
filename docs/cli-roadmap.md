# CLI roadmap analysis: daily workflow, consistency, environment switching

Status: **accepted** (2026-11-20), **revised 2026-11-21** (§8 supersedes the 0.6.0 scope and the branching of §6–§7; §8.1 reorders UI before the manifest), **extended** by §9 (releases after 0.7.0). Contract: ADR 0013 (+ amendment of ADR 0012). Tasks: `docs/mvp-plan.md`
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
1. Command budget accepted: +`status`, +`diff` (`adopt` dropped on 2026-11-21, §8); no `doctor`/`validate`/`standardize`/`get`/`set` commands.
2. Order: 0.4.0 (phase A) → 0.5.0 (phase B) → 0.6.0 (phase C, ADR 0012 reduced); all before the UI reaches `master`.
3. Convention profiles and `standardize` deferred (ADR 0012 amendment).
4. Flags: `--key` (selection), `--force` with `--key` (per-key conflict resolution), `edit --set/--unset/--secret`.
5. UI distribution: inside the `azcfg` tool as `azcfg ui` (a launcher, +1 top-level command on top of the budget),
   not a separate package. The UI reaches `master` as CLI 0.7.0 (UI preview) after 0.6.0 (UI tasks X1, R1).

## 7. Execution (history; current plan in §8 and `docs/mvp-plan.md`)
- 0.4.0 released (PR #53): R0 docs → CW1 cherry-pick C1/C2 → CW2–CW4 Core → CW5–CW9 CLI → CW10.
- 0.5.0: H0 PR CI + `--skip-duplicate` → H1 `HealthReport` → H2 `status --check`; H4 `config project.*` → H3 release.
- Both went through `release/<ver>` branches because every push to `master` touching `src/**` published NuGet; the UI
  lived on `dev`. H0 removed that reason (§8).

## 8. Revision (2026-11-21): less speculative scope, one trunk, more UI
Review of the remaining plan after 0.5.0. The layering (Core / Infrastructure / Cli, ports) stays; no re-architecture.

| Item | Decision | Why |
| --- | --- | --- |
| PR0 remote providers (ADR 0014) | **Defer to stage 11** | `SyncService` uses `IConfigStore` in two places (read, apply): the seam already exists; adding a provider when App Service is actually built is cheap. Manifest v1 no longer needs `RemoteKind` |
| ADR 0012 manifest | **Minimal schema v1** (service, targets, vaults) **that `pull` reads** | Value = onboarding (clone → `pull` works). The amended plan wrote a big public schema nothing consumed |
| `adopt` | **Drop** | Wizard offers "save as `azcfg.json`"; `config set project.shared true` for bound projects. Budget stays 11 (+`ui`) |
| Discovery scanners, drift, tiers, CI model | **Drop until asked** | Model without behaviour = public contract without benefit |
| Secret-like keys in tracked `appsettings*.json` | **Keep**, as a `status --check` warning | Cheap, real security value |
| `LocalSettingsTarget` | **Pull into 0.6.0** | Azure Functions developers are a likely real audience; designed since ADR 0006 |
| `config project.*` | Shipped in 0.5.0; writes `azcfg.json` when it exists | One place for shared settings |
| `release/*` + `dev` + merge gates M2/M3/R1 | **One trunk `master`** | `--skip-duplicate`: a push without a `<Version>` change publishes nothing; `AzCfg.Cli` does not reference `AzCfg.Ui`, so the unfinished UI is not packed until X1 |
| Freed capacity | **UI** | Problems panel, consistency matrix + copy to environment, editable `local.settings.json`, "share with team" |

Execution in orchestrator sessions (one coherent chunk each, 2–4 worktree agents; table in `docs/mvp-plan.md`):
0. one trunk + UI M1 (docs, 0.5.0 to `master`, `dev` merged both ways, delete `dev`/`release/*`);
1. UI core screens U1–U4 ∥ 2. CLI 0.6.0 (manifest, checks, `local.settings.json`);
3. UI extras UX1–UX3 + X1 → Q1, Q2 → 0.7.0 (`azcfg ui`).

### 8.1 Reorder (2026-11-21, later the same day): UI before the manifest
With U3, U4 and LS1 done and the manifest (PC1–PC4) not started, the UI goes first:
- **0.5.1** — `local.settings.json` target (LS1), merged together with the UI screens U3, U4 (not packed yet).
- **0.6.0** — UI preview (`azcfg ui`): U1, U2, UX1–UX3, X1, Q1, Q2, R1. Was 0.7.0.
- **0.7.0** — minimal manifest `azcfg.json` (PC1–PC5) for CLI **and** UI at once (UX4 "share with team"). Was 0.6.0.
- 0.8.0, 0.9.0 unchanged.

Why: only "share with team" of the UI plan needs the manifest; building the `ProjectService`/`ProjectSettingsService`
changes and their UI in one session avoids touching `AzCfg.Ui.Application` twice. Cost: a preview UI without
`azcfg.json` sharing (bindings stay per user, as in 0.5.x). Where §6–§9 say 0.6.0 for the manifest or 0.7.0 for the
UI, this section wins.

Watch, do not act on now: `SyncService` (≈540 lines, 11 public methods) — split only when a feature lands there;
`ConfigEnvironment` is App Configuration-shaped — fine until stage 11.

## 9. Revision (2026-11-20): universal configuration for .NET teams beyond our own
Question: can AzCfg serve third-party .NET teams (App Service / Functions settings, no App Configuration, other key
conventions, plain `appsettings.json` + env vars) without a new architecture? Checked against ADR 0001–0014, this plan
and a real service (ASP.NET Core API on App Service, built config by hand, no App Configuration at runtime).

**Answer: no re-architecture.** The core (ports, `ConfigState` + 3-way diff, plan → apply, `SecretString`, `status --check`,
command budget) is already the universal part. Three additive generalisations are missing:
1. **Remote as an abstraction** — ADR 0014 providers, App Service / Function App settings (stage 11, unchanged design).
2. **Key layout** instead of the hard-wired `<service>:` / `global:` (ADR 0004 becomes the default of `KeyLayout`).
3. **Team-level environments** — optional `environments` in `azcfg.json` (v2) instead of only the per-user `profile.json`;
   onboarding becomes «clone → `az login` → `pull`», and repos of different organisations stop sharing one global list.

**Model.** Remote (App Configuration; App Service/Function App settings) ↔ local target (user-secrets, git-ignored JSON,
`local.settings.json`); Key Vault stays the secret backend behind references; tracked `appsettings*.json` and env vars are
read-only layers (checks, a later UI view), never written. Settings levels: `profile.json` (developer) → `azcfg.json`
(team) → `projects.json` (personal per project); precedence: flag > personal fields > team (shared fields win) > developer
> auto-detection. Runtime precedence of .NET configuration is explained, never changed.

**Moving config between sources** needs no command: `diff dev@app dev`, `promote dev@app dev --key <pattern>` over the
providers; `RemoteDrift` in `status --check` afterwards.

| Item | Decision | Why |
| --- | --- | --- |
| 0.6.0, 0.7.0 | **Unchanged** | No conflict; `local.settings.json` and the v1 manifest are prerequisites |
| Stage 11 (ADR 0014) | **Scheduled as 0.8.0** (was «only on demand») | Deployed config of App Service apps lives in App Settings, often drifting from App Configuration; Functions are the 0.6.0 audience |
| `env@remote`, `promote --key`, `RemoteDrift` | **0.8.0** | Migration App Service → App Configuration without a new command |
| Manifest v2: `environments`, `layout`, `remotes`, `file` | **0.9.0**, ADR 0015 | Teams with other conventions and multi-org developers; additive, v1 stays valid |
| `LocalTargetNotLoaded` check | **0.9.0** | A bound target the app never loads silently «loses» pulled values (seen in practice) |
| Binding without local targets, Key Vault as a remote, effective-config UI view | **Candidates** | Only with a real user; KV-as-remote conflicts with ADR 0014 and needs an amendment |
| Convention profiles, `standardize`, CI/CD generation, env-var/`.env` manager, writing tracked `appsettings*.json`, continuous App Configuration → App Service sync | **Never** | CD or code concerns, large surface, no value over existing .NET mechanisms |

Conflicts and minimal corrections (done as ADR amendments in the stage's first task, not here): ADR 0004 (convention →
default layout), ADR 0008 / UI Q2 (environments may come from the manifest), ADR 0012 (endpoints allowed in the manifest),
ADR 0006 (custom JSON file name, git-ignored only), ADR 0014 (stage 11 scheduled; its «`project.*` never writes the
manifest» is superseded by ADR 0012 amendment 2). No breaking changes: every new field defaults to today's behaviour.
Tasks: `docs/mvp-plan.md` stages 11–12, sessions 4–5.
