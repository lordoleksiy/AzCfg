# Handoff — session 0 / M1 (one trunk, Application delegates to Core)

Temporary orchestrator notes; delete this file in the M1 PR before merge (or right after).
Source of truth for the task: `docs/ui-mvp-plan.md` §10 block **M1**, `docs/mvp-plan.md` «Сесії оркестратора».

## Where the work is
- Worktree: `D:\Programming\.NET\Projects\AzCfg-m1`, branch `feature/ui-m1-one-trunk` (from `origin/dev`).
- Pushed to `origin/dev` (user's OK, 2026-11-20). PR to master / deleting branches only with the user's OK.

## Done (each commit: build + all tests green, `dotnet format --verify-no-changes` clean)
1. `5028970` — merge `origin/master` (CLI 0.4.0 + 0.5.0, PR #57) into the dev line, `--no-ff`.
   - Conflicts (C1/C2 cherry-picks, CLI test fakes, `CLAUDE.md`) → master side.
   - `docs/ui-mvp-plan.md` → dev side (no conflict).
   - `tests/AzCfg.Ui.Tests/Application/Configuration/CountingSecretStore.cs` gained `ExistsAsync`.
   - Auto-merge left a **duplicate** `AddSingleton<TokenCredential>` in `Infrastructure/DependencyInjection.cs` → removed
     (it failed 2 DI tests).
2. `c3202ae` — UI `EnvironmentRules` / `EnvironmentKind` deleted; UI uses `AzCfg.Core.Environments` (CW2).
   UI test `EnvironmentRulesTests` deleted (Core has its own).
3. `f90e700` — `SyncWorkflowService.UnmanagedLocalKeysAsync` → `ISyncService.UnmanagedLocalKeysAsync`; ctor no longer
   takes `IEnumerable<ILocalTarget>`.

4. CompareService → adapter over Core `IConfigComparisonService` (Same/Different + ignored count from Core; UI only adds
   scope/label/secret name and JSON). `ISecretFingerprinter`/`SecretPolicy` no longer used there.
   **Deviation:** ignored count now follows Core = remote-side ignored keys only (test `IgnoredKeys_ExcludedAndCounted`
   expects 1, was 2); Remote↔Remote now honours ignore rules (Core does). Cost: remote config is read twice (Core + adapter).
   **Open (contract, plan §0.6):** UI `Configuration.CompareSide {Local, Remote(Environment)}` duplicates Core
   `Sync.CompareSide {Local, Environment(Env)}`; kept because §7.3 / `ProjectSession` / tests use the UI type. Ask the
   user whether to switch §7.3 to the Core type (touches `ComparisonView`, `ICompareService`, `ProjectSession`, tests).

5. `WorkspaceService.SetVaultAsync` → `IProjectSettingsService.SetAsync(repo, "project.vault.<env>", uri)` then reloads
   the binding (gains Core validation: https vault root, canonical env name). Test now saves the binding first (Core
   requires a bound project — the UI always has one).

6. Per-env probe → Core `IHealthService.CheckEnvironmentAsync(env, service?, ct)`; `EnvironmentCheck(Name, Access, Error)`
   with `EnvironmentAccess { Ok, AuthRequired, Forbidden, Unreachable }` (`Reachable` is computed, kept in JSON).
   Classification via a new Core port `IAccessErrorClassifier` → Infrastructure `AzureAccessErrorClassifier` over
   `AzureErrors` (fixed texts for auth/403/network, type name otherwise — never a message). `CheckAsync` reuses it.
   UI `AzureStatusService` now takes `IHealthService` (not `IConfigStore`) and keeps cache, events, `ConnectionState`
   mapping and the account check. CLI: `status --check --json` gains `environments[].access`; human output shows the
   fixed reason instead of the type name for auth/403/network (CHANGELOG [Unreleased]). The CLI scenario host registers
   the classifier by hand (it does not use `AddAzCfg`).

Test counts after step 6: Core 735, Cli 266, Infrastructure 206, Ui 407.

## Remaining — briefs for agents without context

State: everything above is pushed to `origin/dev` (fast-forward from `feature/ui-m1-one-trunk`). Start each task with
`git fetch && git switch -c feature/<id>-<slug> origin/dev`, PR **into `dev`** (not master — M1 lands in master as one PR,
task F). After each step: `dotnet build AzCfg.slnx`, `dotnet test AzCfg.slnx`, `dotnet format AzCfg.slnx --verify-no-changes`.
Rules: `CLAUDE.md` (TDD for Core, Core has no IO/Azure, `--json` for CLI output, CHANGELOG for user-visible changes).
M1 goal (`docs/ui-mvp-plan.md` §10 M1): `AzCfg.Ui.Application` has no rule/type duplicated from Core — it only adapts.
Tasks A–C are independent; D after A–C; E–G in order.

### A. Re-bind through Core project settings (`WorkspaceService.BindAsync`)
- File: `src/AzCfg.Ui.Application/Workspace/WorkspaceService.cs` (`BindAsync`), tests in
  `tests/AzCfg.Ui.Tests/Application/Workspace/WorkspaceServiceTests.cs`.
- Problem: on re-bind the UI calls Core `IProjectService.BindAsync` (fresh binding) and then restores vaults,
  `ActiveEnvironment` and ignore by hand. Core rule (ADR 0014, `ProjectSettingsService`): changing `project.service`
  resets `ActiveEnvironment` and deletes snapshots. The UI keeps both → divergence.
- Do: first bind (no binding yet) stays `projects.BindAsync`. Existing binding → `IProjectSettingsService.SetAsync(repo, key,
  value)` for each changed key: `project.service`, `project.values`, `project.secrets` (value `<target>:<csproj path>`,
  target names `user-secrets` / `appsettings-local`; see `ProjectLookup.KindNames` in Core and how `SetVaultAsync` in the
  same file already does it). Then reload the binding. Drop the manual restore code.
- Tests first: re-bind with a new service → `ActiveEnvironment` is null and snapshots are gone; re-bind changing only the
  values target keeps `ActiveEnvironment`; vaults and ignore survive.

### B. `CompareSide` duplicate — needs the user's decision first
- UI `src/AzCfg.Ui.Application/Configuration/CompareSide.cs` `{Local, Remote(Environment)}` duplicates Core
  `AzCfg.Core.Sync.CompareSide {Local, Environment(Env)}`. Switching to the Core type changes the contract in
  `docs/ui-mvp-plan.md` §7.3 (touches `ComparisonView`, `ICompareService`, `ProjectSession`, UI tests).
- **Ask the user** (plan §0.6). If yes: replace the UI type with the Core one, update §7.3, keep tests green. If no: leave
  it and note the exception in §7.3.

### C. Final duplication grep
- Search `src/AzCfg.Ui.Application` for logic that Core already has: access probing (done in step 6), binding edits
  (task A), environment rules, ignore rules (`IgnoreRules`), key mapping (`KeyMapper`), secret naming, missing-key
  reports, sync summaries. For each hit: delegate to Core or write in this file why it stays. No behaviour change.
- Add to the U2 brief (header summary must come from `ISyncService.StatusAsync` / `SyncSummary`, not UI code).

### D. Manual check of the screens
- `dotnet run --project src/AzCfg.Ui -- --demo`, open each screen (workspace, environments + status dots, configuration,
  compare, sync). Compare with `dev` before M1 (`git switch --detach e61bb6b`). Expected: same screens; environment
  status texts for auth/403/network now come from Core (same wording as `AzureErrors.Describe`).
  If it cannot be run, write "not verified" in the PR.

### E. Close M1 in the docs
- Tick M1 in `docs/ui-mvp-plan.md` §0.7 and §10; tick session 0 in `docs/mvp-plan.md` («Сесії оркестратора») if listed.
- Delete this file.

### F. PR `dev` → `master` (user's OK)
- No `<Version>` change → nothing is published. Body: steps 1–6 + A–E, the deviations (step 4 ignored-count,
  step 6 CLI `--json` field), test counts.

### G. Clean-up after merge (only with the user's explicit OK)
- Delete `dev` and `release/*` (local + origin), `git worktree prune`, remove stale `worktree-agent-*` branches,
  the `.claude/worktrees/*` worktrees whose branches are merged, and `../AzCfg-m1`, `../AzCfg-dev-docs`.

## Resume
```
git fetch; git switch -c feature/<id>-<slug> origin/dev
dotnet build AzCfg.slnx -v q --nologo; dotnet test AzCfg.slnx --no-build -v q --nologo
```
