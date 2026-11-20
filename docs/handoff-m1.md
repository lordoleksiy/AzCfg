# Handoff — session 0 / M1 (one trunk, Application delegates to Core)

Temporary orchestrator notes; delete this file in the M1 PR before merge (or right after).
Source of truth for the task: `docs/ui-mvp-plan.md` §10 block **M1**, `docs/mvp-plan.md` «Сесії оркестратора».

## Where the work is
- Worktree: `D:\Programming\.NET\Projects\AzCfg-m1`, branch `feature/ui-m1-one-trunk` (from `origin/dev`).
- **Not pushed** yet. Push / PR / deleting branches only with the user's OK.

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

## Remaining (M1 scope (2), §7.3 signatures stay; a signature that must change → plan §0.6)
- [x] `Configuration/CompareService` → adapter over `IConfigComparisonService` (see step 4; `CompareSide` question open).
- [x] FR-S7 counts: Application computes none yet (header is U2) → nothing to delegate. **Add to U2 brief:** header
      summary must come from `ISyncService.StatusAsync` (`SyncSummary`), not from UI code.
- [x] `Environments/AzureStatusService.CheckEnvironmentAsync` → Core (step 6, decided by the user: a Core method).
- [x] `SetVaultAsync` → `IProjectSettingsService` (step 5).
- [ ] `WorkspaceService.BindAsync` re-bind: UI restores vaults + **ActiveEnvironment** + ignore after Core `BindAsync`
      (which builds a fresh binding). **Rule divergence:** Core `project.service` change resets ActiveEnvironment and deletes
      snapshots; the UI re-bind with a different service keeps ActiveEnvironment and snapshots. Fix: for an existing binding,
      re-bind through `IProjectSettingsService.SetAsync` (`project.service`, `project.values`, `project.secrets` —
      target names `user-secrets`/`appsettings-local`, Core resolves the csproj; check how a `LocalTargetBinding` from
      the UI maps to a name + project) and keep `projects.BindAsync` only for the first bind. Add a UI test:
      re-bind with a new service → ActiveEnvironment null, snapshots gone.
- [ ] Final grep: no type/rule duplicated between `AzCfg.Ui.Application` and Core (access probe, binding edit).
- [ ] `dotnet run --project src/AzCfg.Ui -- --demo` — screens unchanged (manual; if not possible → "not verified").
- [ ] Tick M1 in `docs/ui-mvp-plan.md` §0.7 and §10; tick session 0 in `docs/mvp-plan.md` if it tracks it.
- [ ] Delete this file; push branch; PR `feature/ui-m1-one-trunk` → `master` (no `<Version>` change → nothing published).
- [ ] After merge, with the user's OK: delete `dev`, `release/*` (local + origin), `git worktree prune`, remove stale
      `worktree-agent-*` branches and the `../AzCfg-m1` worktree.

## Resume
```
cd D:\Programming\.NET\Projects\AzCfg-m1
git log --oneline -4
dotnet build AzCfg.slnx -v q --nologo; dotnet test AzCfg.slnx --no-build -v q --nologo
```
Then take the first unchecked item above.
