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

Test counts after step 4: Core 729, Cli 266, Infrastructure 198, Ui 403.

## Remaining (M1 scope (2), §7.3 signatures stay; a signature that must change → plan §0.6)
- [x] `Configuration/CompareService` → adapter over `IConfigComparisonService` (see step 4; `CompareSide` question open).
- [ ] FR-S7 counts (project header summary) → `ISyncService.StatusAsync` (`SyncSummary`). Find where the UI computes counts
      (`grep -rn "Summary\|Count" src/AzCfg.Ui.Application/Sync src/AzCfg.Ui.Application/Configuration`).
- [ ] `Environments/AzureStatusService.CheckEnvironmentAsync` (per-env probe) → Core `IHealthService` (H1/H2);
      the account/token check (`CheckAccountAsync`, `JwtClaims`) may stay.
- [ ] Project settings (service, values/secrets targets, vault per env) → `IProjectSettingsService`
      (`ListAsync/GetAsync/SetAsync(repoPath, key, value)`, keys `project.*`, ADR 0014);
      `WorkspaceService.BindAsync` keeps only what Core does not do (G5); `SetVaultAsync` likely →
      `SetAsync(repo, "project.vault.<env>", uri)`.
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
