# 0012. Project Configuration: manifest, convention profiles, discovery, `adopt`, `standardize`

Status: accepted, **amended 2026-11-20** (see «Amendment» at the end): CLI 0.6.0, stage 10 of `docs/mvp-plan.md`,
after ADR 0013 (0.4.0) and the health stage (0.5.0). Schema v1 remote source kinds are amended by ADR 0014.

## Context
- Today AzCfg is zero-config (ADR 0001): everything is auto-detected and the choice is remembered in user state
  (`projects.json`, ADR 0006). Nothing in the repo says *how the project's configuration is meant to be organised*, so
  a teammate, CI or the UI has to re-detect it, and an unfamiliar project cannot be opened without the `init`/`pull`
  wizard making assumptions.
- Conventions (key naming ADR 0004, secret naming, local targets ADR 0006, environment modes ADR 0008, `.gitignore`
  ADR 0009) are hard-coded. There is no way to state "my preferred conventions" or to bring an existing project in line.
- Secrets live in three different places with different rules (developer machine, CI, deployed app) and the model does
  not distinguish them.
- A CI/CD description (GitHub Actions environments, deployment targets) is wanted later; the model should be ready,
  the automation is out of scope now.
- The UI (ADR 0010/0011) must consume the same Core capabilities; it must not shape them.

## Decision
### Terminology
- **Convention profile** (`ConventionProfile`; user-facing name *Global Configuration Profile*) — a per-user (shareable
  file) set of preferred conventions. Not to be confused with `UserProfile` (`profile.json`: environments/mode), which
  is unchanged.
- **Project manifest** (`ProjectManifest`) — a git-tracked file in the repo that describes the project's configuration
  *structure*. It is the shared "desired" description; `ProjectBinding` (user state) stays the per-user choice.
- **Discovery** — read-only inspection of a repo (and, through existing ports, Azure) that yields a `DiscoveryReport`.
- Resolution order for a project: manifest (if present) → `ProjectBinding` (user state) → auto-detection. The manifest
  is optional; zero-config keeps working (ADR 0001).

### Manifest file
- File `azcfg.json` at the repo root (JSON with comments and trailing commas allowed; BCL `System.Text.Json`, no new
  package). It **supersedes the unimplemented `.azcfg.yaml` escape hatch** mentioned in ADR 0001 / architecture: a
  YAML parser would be a new dependency for no benefit, and the manifest is the generalisation of that escape hatch.
- Schema v1 (`ProjectManifest`, sealed immutable records in Core `Manifest/`):
  ```csharp
  public sealed record ProjectManifest(
      int SchemaVersion,                               // 1
      string Service,                                  // service prefix (ADR 0004)
      ManifestProfileRef? Profile,                     // (Name, Version) of the convention profile last applied
      IReadOnlyList<ManifestEnvironment> Environments, // (Name, Label?, IsProtected, StoreRef?)
      IReadOnlyList<ConfigSource> Sources,             // where configuration lives
      IReadOnlyList<SecretRef> Secrets,                // references only, see below
      IReadOnlyList<KeySpec>? Keys = null,             // optional structure: (Path, Kind Value|Secret, Required, Scope)
      IReadOnlyList<string>? Ignore = null,            // same patterns as ProjectBinding.Ignore (ADR 0008)
      CiConfig? Ci = null,                             // reserved, see CI/CD readiness
      IReadOnlyList<DeploymentTarget>? Deployments = null);

  public enum ConfigSourceKind { AppConfiguration, KeyVault, UserSecrets, AppSettingsFile, LocalSettingsFile,
                                 EnvironmentVariables, GitHubSecrets }
  public sealed record ConfigSource(string Name, ConfigSourceKind Kind, string? Location, LocalFileFormat? Format,
                                    ConfigRole Role /* Values | Secrets | Both */, SecretTier? Tier = null);
  ```
- **The manifest never contains values** — neither secret nor plain. `SecretRef(Name, Tier, SourceName, Pointer)` holds
  a pointer (`Pointer` = key path / Key Vault secret *name*, never a value). The model has no value property;
  `ManifestValidator` additionally rejects unknown properties (strict schema), so a pasted `"value"` / `"password"` is
  an error, not silently ignored. Store endpoints and vault names are allowed (not secrets).
- Unknown future fields: `SchemaVersion` greater than supported → `ManifestValidator` error with an upgrade hint; the
  file is never rewritten by an older tool.
- Port `IProjectManifestStore` (`LoadAsync(repoPath)` → `ProjectManifest?`, `SaveAsync`, atomic write like
  `JsonUserStateStore`); Infrastructure `FileProjectManifestStore`.

### Secret tiers
- `enum SecretTier { Local, Ci, Deployed }`:
  | Tier | Meaning | Convention store | Auth |
  | --- | --- | --- | --- |
  | `Local` | developer machine | .NET User Secrets (`ConfigSourceKind.UserSecrets`) | the developer's OS profile |
  | `Ci` | build/test pipeline | GitHub Secrets (`GitHubSecrets`) — only for what OIDC cannot cover | OIDC federation preferred; no stored Azure credential |
  | `Deployed` | running app | Azure Key Vault (`KeyVault`) | Managed Identity |
- Validator rules: a `Secrets` entry's `Tier` must match its source kind (`Local` ↔ `UserSecrets`, `Deployed` ↔
  `KeyVault`, `Ci` ↔ `GitHubSecrets`); a secret may not point at `AppSettingsFile`/`LocalSettingsFile` unless that
  file is git-ignored (same safety check as ADR 0006 `IsSafeForSecrets`).
- Existing behaviour is unchanged: today's `SecretsTarget` is the `Local` tier, KV references in App Configuration are
  the `Deployed` tier. This ADR only names and validates them.

### Convention profiles
- `ConventionProfile(Name, Version, Naming, Layout, Environments, Secrets, Providers)`:
  - `Naming` — scope prefixes (`<service>:` / `global:`), KV name separator (`--`), key casing rule;
  - `Layout` — preferred values target and secrets target kinds, file names (`appsettings.local.json`), whether local
    files must be git-ignored, `//@global` marker use (ADR 0009);
  - `Environments` — mode (`stores|labels`), canonical names, protected-name pattern;
  - `Secrets` — allowed store kind per `SecretTier`, `ForbidSecretsInTrackedFiles`;
  - `Providers` — preferred config provider (`AppConfiguration`) and secret provider (`KeyVault`).
- Built-in `azcfg-default` encodes exactly the current conventions (ADR 0004/0006/0008/0009), so `standardize` on an
  already conforming project is a no-op and nothing changes for existing users.
- Storage: user state `%APPDATA%/azcfg/profiles/<name>.json` via `IConventionProfileStore` (Infrastructure
  `JsonConventionProfileStore`); a profile can also be loaded from any file path (`--profile <name|path>`) so a team
  can share one in a repo. No remote fetching. Active profile name: new field
  `UserProfile.ConventionProfile` (default `"azcfg-default"`, old `profile.json` reads without migration), settable by
  `azcfg config set profile <name>`.

### Discovery
- Core `ProjectConfigService.DiscoverAsync(repoPath, ct)` → `DiscoveryReport(IReadOnlyList<DiscoveredSource>
  Sources, IReadOnlyList<DiscoveredEnvironment> Environments, IReadOnlyList<DiscoveryFinding> Findings)`.
  `DiscoveredSource(Kind, Location, IReadOnlyList<string> KeyPaths, bool IsGitTracked, Confidence)`.
- **Discovery returns key paths only, never values** (a secret-bearing file is read in memory to enumerate paths and
  the values are dropped immediately). Reports, logs and `--json` are therefore value-free by construction.
- Port `IConfigSourceScanner` (Infrastructure `FileConfigSourceScanner`) scans the repo: `appsettings*.json`,
  `local.settings.json`, user-secrets (`UserSecretsId` → existing `UserSecretsTarget`), `launchSettings.json`
  environment variables, `.env*` files, `docker-compose*` `environment:` keys. Azure side reuses `IResourceDiscovery`
  and `IConfigStore.ListServicePrefixesAsync` — no new Azure code.
- Process environment variables of the machine are **not** scanned (private and noisy); only names referenced by the
  repo. `.github/workflows/*.yml` is **not** parsed in this stage (needs YAML); the `Ci` section is filled by the user
  or by a later stage.
- `DiscoveryFinding(Severity, Code, Location, Message)` — hints only: e.g. `SecretLikeKeyInTrackedFile` (key *name*
  matches `password|secret|token|key|connectionstring`, file is git-tracked), `NoUserSecretsId`, `MultipleServiceCandidates`.
- Discovery shares the existing `ProjectService` heuristics for service-name matching (no duplicated logic).

### `azcfg adopt`
- Registers an existing project **without restructuring it**: Discovery → `AdoptPlan` (proposed manifest +
  `ProjectBinding`) → preview → confirmation → writes `azcfg.json` and the user-state binding. **It never edits,
  moves or deletes any existing project file** (not even `.gitignore`).
- Idempotent: if `azcfg.json` exists, the plan is a merge shown as a diff; hand-written fields (`Keys`, `Ci`,
  `Deployments`, comments are not preserved but unknown-to-adopt sections are carried over unchanged) are kept.
- Flags: `--yes` (skip confirmation), `--json` (the `AdoptPlan` record as-is, no interaction), `--project <path>`.
- Core never prompts (ADR 0003): ambiguity (several service candidates, several sources of one role) is returned as
  data in `AdoptPlan.Questions`; the CLI asks.

### `azcfg standardize`
- Applies a convention profile to the project: `azcfg standardize [--profile <name|path>] [-S]`.
- Flow: actual (discovery + manifest) vs desired (profile) → `MigrationPlan(IReadOnlyList<MigrationStep> Steps,
  IReadOnlyList<SyncWarning> Warnings)` → **preview (always shown)** → confirmation → apply. `--yes` skips the prompt;
  `--json` prints the plan without applying. Apply without confirmation is impossible.
- `MigrationStep` is a data union (value-free): `UpdateManifest`, `CreateFile`, `EnsureGitIgnored`,
  `CopyKeys(FromSource, ToSource, KeyPaths)`, `RenameKeys(Source, (From, To)[])`, `SetEnvironmentMode`. Each step has
  `Description`, `Touches` (paths) and `IsDestructive`.
- **Local only**: standardize changes local files, the manifest and user state. It never writes to App Configuration
  or Key Vault; reconciling remote keys is the job of the normal `plan`/`push` afterwards.
- **No deletion by default**: moving values is a *copy* (read through `ILocalTarget` in memory, written to the new
  target, verified by re-reading); source values stay. Removing the source is an explicit second opt-in
  (`--prune`, later stage) and, for sources outside git (user-secrets), still requires confirmation.
- Secret safety: a step that would put secrets into a non-ignored file fails the plan (same rule as ADR 0006);
  plan, preview, logs and `--json` carry key paths and step kinds only.
- Core: `StandardizePlanner` (static, pure, TDD) builds the plan; `ProjectConfigService.ApplyStandardizeAsync`
  orchestrates; port `IMigrationExecutor` (Infrastructure) performs file-level steps via `ILocalTarget` and the
  manifest store.
- `-S` (working interpretation, see open questions): `--select` — interactive per-step selection in the preview.
  Without it all steps are applied after one confirmation. The Core API takes the selected step ids, so `-S` is purely
  a CLI concern.

### Lifecycle: compare, validate, sync
- `ProjectConfigService.ValidateAsync(repoPath)` → `ValidationReport(Errors, Warnings)`: manifest schema/rules
  (secret tiers, cross-references, no value-like fields) plus profile conformance.
- `ProjectConfigService.CompareAsync(repoPath, profile)` → `DriftReport(IReadOnlyList<DriftItem>)`: desired (manifest +
  profile) vs actual (discovery), `DriftKind { Missing, Unexpected, Mismatch }`, value-free. Same data feeds
  `validate`, the `standardize` preview and the future UI.
- CLI `azcfg validate [--json]` prints both; exit code ≠ 0 on errors (usable in CI later).
- `pull`/`push` are unchanged in this stage. A later stage lets `ProjectService.ResolveAsync` pre-fill the binding from
  the manifest (resolution order above); full manifest-driven sync (sources → targets mapping) comes after that.

### CI/CD readiness (model only)
- `CiConfig(CiProvider Provider /* GitHubActions */, IReadOnlyList<CiEnvironment> Environments)`;
  `CiEnvironment(Name, MapsToEnvironment, AuthMode /* Oidc | StoredSecret */, IReadOnlyList<string> SecretRefs,
  IReadOnlyList<string> VariableNames)`;
  `DeploymentTarget(Name, Kind /* AppService | FunctionApp | ContainerApp | Other */, Environment, Identity /*
  ManagedIdentity | None */, ConfigSourceNames)`.
- Parsed, serialised, round-tripped and cross-validated (referenced environments and secret refs exist; `Oidc` is
  consistent with the `Ci` tier) — **nothing else**: no GitHub API, no workflow generation, no deployment automation,
  no federated-credential setup. That is a later stage built on the same model.

### Layering and UI
- Core: `Manifest/`, `Profiles/`, `Discovery/`, `Standardize/` (records, validator, planner, drift comparer),
  use case `ProjectConfigService`, ports `IProjectManifestStore`, `IConfigSourceScanner`, `IConventionProfileStore`,
  `IMigrationExecutor`. Core stays free of Azure SDK, file IO and console; the new service returns data, the client
  decides (ADR 0003).
- Infrastructure implements the ports and registers them in `AddAzCfg()`.
- CLI: `adopt`, `standardize`, `validate` with `--json` (the use-case record as-is). The UI will call the same
  `ProjectConfigService`; no UI-specific API is added and `docs/ui-mvp-plan.md` is not changed by this ADR.

## Open questions (default decisions)
- Meaning of `-S` in `azcfg standardize -S` — `--select` (choose steps in the preview). If `--secrets` (secret-handling
  steps only) or `--strict` was meant, only the CLI flag changes; Core already filters by step kind/id.
- Manifest format/name — `azcfg.json` over `.azcfg.yaml` (no new dependency); a YAML reader can be added later if needed.
- Does the manifest hold per-environment store endpoints? — optional `StoreRef` only (logical name or endpoint); real
  endpoints stay in user state by default so a repo does not leak infrastructure it does not need to.
- Should `adopt` also run `.gitignore` fixes? — no; that is a `standardize` step.

## Consequences
- A repo can carry a value-free description of its configuration; teammates, CI and the UI read it instead of
  re-detecting.
- Existing users see no change: no manifest → zero-config; `azcfg-default` equals today's conventions.
- Three new commands and one Core service; `pull`/`push` untouched this stage.
- The manifest is git-tracked, so its schema is a public contract: `SchemaVersion` and strict validation are required
  from the first release.
- Release: CLI 0.4.0.

## Amendment (2026-11-20)
Decided with the owner after `docs/cli-roadmap.md`; supersedes the conflicting parts above.
- **Order:** the daily-workflow release (ADR 0013, CLI 0.4.0) and the health release (`status --check`, 0.5.0) come
  first. This ADR ships as CLI **0.6.0** (stage 10), via `release/0.6.0` from `master`, before the UI reaches `master`.
- **Command budget:** no `azcfg validate` command — `ValidateAsync`/`CompareAsync` feed the `HealthReport` shown by
  `azcfg status --check` (exit code ≠ 0 on errors). `azcfg adopt` stays.
- **Deferred** (not in 0.6.0): convention profiles (`ConventionProfile`, `IConventionProfileStore`, `profiles/`,
  `UserProfile.ConventionProfile`) and `azcfg standardize` / `MigrationPlan` / `IMigrationExecutor`. Drift is computed
  against the manifest only. The open question about `-S` is moot until `standardize` is scheduled.
- Unchanged: manifest model and validation, discovery, value-free guarantees, CI/CD model as data only.
