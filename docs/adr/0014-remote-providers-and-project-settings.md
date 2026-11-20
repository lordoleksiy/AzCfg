# 0014. Remote config providers and editable project settings

Status: accepted (2026-11-20), **amended 2026-11-21** (see «Amendment» at the end). Editable project settings shipped
in CLI 0.5.0 (stage 9). The provider refactor is **no longer** a prerequisite of 0.6.0: it moves to stage 11 together
with App Service / Function App settings, and is scheduled only when a user needs them.

## Context
- Project settings exist only as `ProjectBinding` in user state (`projects.json`, ADR 0006): `Service`, `ValuesTarget`,
  `SecretsTarget`, `VaultByEnvironment`, `ActiveEnvironment`, `Ignore`. It is created once by the first `pull`/`use`
  wizard. After that only `Ignore` (`azcfg ignore`) and `ActiveEnvironment` (`use`) can change; `--service` is silently
  ignored when a binding exists; service and local targets can only be changed by hand-editing `projects.json`.
- The remote side is hard-wired to App Configuration + Key Vault: `SyncService` depends on `IConfigStore` and
  `ISecretStore` directly; `ConfigEnvironment` = `StoreEndpoint` + `Label` (App Configuration concepts); `service:` /
  `global:` scoping (ADR 0004) lives in `KeyMapper`/`ScopeMerger`.
- Wanted later: App Service and Function App application settings (and possibly other providers). They differ from App
  Configuration: one resource **per service** (not a store shared by services), flat keys (`A__B`), no labels, no
  per-key ETag (the whole settings dictionary is replaced by one ARM `PUT`), deployment slots, Key Vault references as
  `@Microsoft.KeyVault(SecretUri=…)`, platform keys (`WEBSITE_*`, `FUNCTIONS_*`, `AzureWebJobs*`), and a write restarts
  the app.
- ADR 0012's manifest (`azcfg.json`, git-tracked, CLI 0.6.0) is a public schema once shipped, so the provider model must
  be in schema v1. Doing the refactor after 0.6.0 would mean a schema v2 migration.

## Decision
### CLI 0.4.0 contracts that stay unchanged
- Commands and command budget (ADR 0013): no new top-level command in this ADR. Everything is a `config` key or a flag.
- `ISyncService` / `IConfigComparisonService` signatures (used by the CLI and by the UI on `dev`): every existing method
  keeps operating on the **primary remote** (App Configuration) of `(binding, env)`. New capabilities are overloads or
  optional parameters, never changed signatures.
- `IConfigStore`, `ISecretStore`, `ILocalTarget` ports stay as they are (no break for `dev`/UI Infrastructure).
- On-disk state: `profile.json`, `projects.json` and `snapshots/<repo-hash>/<env>.json` keep their shape; new
  properties are optional with defaults, so files written by 0.4.0 load unchanged and 0.4.0 can still read files
  written by later versions for App Configuration-only projects.
- `--json` outputs (`status`, `diff`, `plan`, `config list`, `ignore list`) only gain properties; nothing is renamed or
  removed.
- Key canonical form in Core stays `Key:Path` with `ConfigScope`; `--key` patterns (`*`, optional `global:`) and the
  `{ "@kv": … }` edit format keep their meaning for every provider.
- `use`: G20 first-activation confirmation, `--confirm <ENV>` for protected environments, blocked-key listing — reused
  as-is by the rules below.

### Editable project settings (CLI 0.5.0)
- `azcfg config` gets project-scoped keys with the `project.` prefix, resolved for `--project <PATH>` (default: current
  directory). User keys (`mode`, `defaultLabel`, `gitignoreLocalFiles`, `env.<name>.*`) are unchanged.

  | Key | Value | Settable |
  |---|---|---|
  | `project.service` | service prefix (ADR 0004) | yes |
  | `project.values` | `<target>[:<project path>]` | yes |
  | `project.secrets` | `<target>[:<project path>]` | yes |
  | `project.vault.<env>` | Key Vault URI | yes (empty = unset) |
  | `project.active` | environment name | no — use `azcfg use` |
  | `project.ignore` | patterns, comma-separated | no — use `azcfg ignore` |

  `<target>` = `user-secrets` \| `appsettings-local` \| `local-settings` (the last one is accepted only once its
  `ILocalTarget` exists). The format is derived from the kind as today (`LocalSettings` → `FlatDictionary`, else
  `NestedJson`); the project path defaults to the current binding's path.
- Core: `IProjectSettingsService` (`ListAsync(repoPath)`, `GetAsync(repoPath, key)`, `SetAsync(repoPath, key, value)`)
  returning the existing `UserSetting(Key, Value)` record so `config --json` keeps one shape. `config list` inside a
  bound project prints user keys then `project.*` keys; outside one, only user keys. No binding + `config set project.*`
  → error «not bound yet, run `azcfg pull`».
- Rules (Core, TDD):
  - Changing `project.service` sets `ActiveEnvironment = null` and drops the repo's snapshots (they describe the old
    service). The next `use`/`pull` is a first activation, so the 0.4.0 G20 confirmation protects local files.
  - Changing `project.values`/`project.secrets` does **not** move data (moving is `standardize`, deferred by the ADR
    0012 amendment). The result carries a hint: run `azcfg pull --force` to fill the new target. Secret-safety rule of
    ADR 0006 still applies: a secrets target that is a git-tracked, non-ignored file is rejected.
  - Values are validated with the same rules as the `pull` wizard (service name syntax, existing csproj, KV URI).
- `pull`/`use --service X` on a project bound to another service → error naming the current service and the
  `azcfg config set project.service X` command (today it is silently ignored). Same service → no-op.

### Remote providers (prerequisite of CLI 0.6.0, no user-visible change)
- Core model (`Core/Remotes`):
  ```csharp
  public enum RemoteKind { AppConfiguration, AppServiceSettings } // Function App = AppServiceSettings (same ARM API)

  [Flags] public enum RemoteCapabilities
  {
      None = 0, Labels = 1, PerKeyConcurrency = 2, Delete = 4, KeyVaultReferences = 8, ServiceScopes = 16,
      RestartsOnWrite = 32,
  }

  public sealed record RemoteLocation(RemoteKind Kind, string Address, string? Label = null, string? Slot = null);

  public interface IRemoteConfigProvider
  {
      RemoteKind Kind { get; }
      RemoteCapabilities Capabilities { get; }
      IReadOnlyList<string> PlatformIgnore { get; }          // built-in ignore patterns, e.g. WEBSITE_*
      IAsyncEnumerable<ConfigEntry> ReadAsync(RemoteLocation location, string service, CancellationToken ct);
      Task<ApplyResult> ApplyAsync(RemoteLocation location, string service, IReadOnlyList<Change> changes, CancellationToken ct);
  }
  ```
- Providers are **Core adapters over Infrastructure ports**, because key mapping, scopes and labels are business rules
  (ADR 0004, 0008):
  - `AppConfigurationProvider(IConfigStore)` — today's behaviour moved out of `SyncService` unchanged: `service:`/`global:`
    scopes, labels and `WriteLabel`, per-key `If-Match`. Capabilities: all except `RestartsOnWrite`.
  - `AppServiceSettingsProvider(IAppSettingsStore)` (stage 11) — new port `IAppSettingsStore`
    (`ReadAsync(resourceId, slot) → AppSettingsSnapshot(IReadOnlyDictionary<string,string> Settings, string ETag)`,
    `ReplaceAsync(resourceId, slot, settings, expectedETag)`); Infrastructure implements it via ARM. Mapping:
    `A__B` ↔ `A:B` (reads accept `:` too, writes always use `__`); `@Microsoft.KeyVault(SecretUri=…)` ↔
    `ConfigValue.KeyVaultRef`; everything is `ConfigScope.Service`. Capabilities: `Delete`, `KeyVaultReferences`,
    `RestartsOnWrite`. Platform keys come from `PlatformIgnore`.
- Rules for providers without a capability (enforced in Core when planning, so `plan` shows them before `push`):
  - no `ServiceScopes` → `@global` / `global:` entries become `ApplyBlocker`s;
  - no `Labels` → label settings are ignored for that remote;
  - no `PerKeyConcurrency` → read-modify-write: re-read, check every changed key still equals the plan's `Before`
    (else conflict, same as an ETag mismatch), then replace the whole dictionary with the resource ETag;
  - `RestartsOnWrite` → `SyncWarningKind.RestartsApp` in the plan; protected environments still require typing the
    environment name.
- `SyncService` depends on `IEnumerable<IRemoteConfigProvider>` (+ `ISecretStore`) instead of `IConfigStore`; a
  `RemoteResolver` (static, pure) turns `(UserProfile, ProjectBinding, environment, remote name?)` into a
  `RemoteLocation`. The App Configuration path must pass every existing test unchanged — that is the acceptance
  criterion of the refactor.

### Where remotes are configured
- Environments stay **per user** (`UserProfile`): name, `IsProtected`, and the App Configuration store/label, because a
  store is shared by many services. This is the implicit **primary remote**, named `appconfig`; existing bindings need
  nothing new.
- Per-service resources (App Service, Function App) are **per project**: optional `ProjectBinding.Remotes`:
  ```csharp
  public sealed record RemoteBinding(string Name, RemoteKind Kind,
      IReadOnlyDictionary<string, RemoteLocation> LocationByEnvironment);
  ```
  Configured via `config set project.remote.<name>.<env> <resource-id>[@<slot>]` (stage 11), `kind` inferred from the
  resource type. No new command.
- Selecting a remote: `--remote <name>` flag on `pull`, `plan`, `push`, `status`, `diff`, `edit` (stage 11). Without it
  every command uses `appconfig`, exactly as in 0.4.0. Snapshots for a non-primary remote live in
  `snapshots/<repo-hash>/<env>@<remote>.json`; the primary keeps `<env>.json`.
- `diff` across remotes («what is deployed vs what is in App Configuration») is a natural extension
  (`CompareSide.Environment` + remote name) and is decided when stage 11 is planned.

### Manifest alignment (amends ADR 0012 schema v1)
- `ConfigSourceKind` keeps its local kinds and uses the provider names for remote kinds: `AppConfiguration`, `KeyVault`,
  `AppServiceSettings` (replaces the idea of App Service only as a `DeploymentTarget`). `ConfigSource` may carry
  `Remote` (the `RemoteBinding.Name`) so the manifest and `ProjectBinding.Remotes` describe the same thing; real
  resource ids stay optional in the manifest (ADR 0012 open question on endpoints still holds).
- `DeploymentTarget.ConfigSourceNames` refers to these sources; the CI/CD model stays data only.
- Resolution order stays manifest → `ProjectBinding` → auto-detection; `project.*` config keys edit the binding (user
  state), never the manifest.

## Open questions (default decisions)
- Ship editable project settings in 0.5.0 or as 0.4.1? — **0.5.0**: new `config` keys are a feature (minor bump), and
  0.5.0's `status --check` can then report a mismatched binding with a fix command.
- One interface for read + apply, or separate read/write providers? — **one**, with `RemoteCapabilities`; a read-only
  provider simply lacks `Delete` and rejects `ApplyAsync`.
- Key Vault as a provider? — **no**: KV stays the secret backend behind `ConfigValue.KeyVaultRef` for every remote
  (`ISecretStore`), not a remote of its own.
- Container Apps, environment variables, GitHub secrets as remotes — out of scope; the model allows adding a `RemoteKind`
  without touching Core sync logic.
- Global keys for App Service — not supported (no `ServiceScopes`); a user who needs them pushes the effective
  (merged) config, decided with stage 11.

## Consequences
- Users can fix a wrong service or local target without editing `projects.json`, and a conflicting `--service` is
  reported instead of being ignored.
- 0.4.0 behaviour, `--json` shapes and on-disk files stay compatible; the UI on `dev` keeps compiling (ports unchanged).
- The 0.6.0 manifest is born with the provider model, so App Service / Function App settings need no schema v2.
- Adding App Service / Function App later is: one Infrastructure port + one Core provider + `local.settings.json`
  target, with sync, plan, conflicts, secret safety and `--key` reused.
- Cost: a Core refactor of `SyncService` before 0.6.0, guarded by the existing test suite.

## Amendment (2026-11-21)
Decided with the owner after reviewing the remaining scope (`docs/cli-roadmap.md` §8).
- **Provider refactor deferred to stage 11.** The argument "the manifest schema v1 must know `RemoteKind`" no longer
  holds: ADR 0012 Amendment 2 reduces schema v1 to service, targets and vaults, and later fields arrive by bumping
  `SchemaVersion` (v1 files stay valid, nothing to migrate). `SyncService` touches `IConfigStore` in exactly two places
  (read and apply), so introducing `IRemoteConfigProvider` when App Service settings are actually built costs little and
  is guarded by the same "all existing tests pass unchanged" criterion. Until then no `Core/Remotes` types exist.
- **Manifest interplay.** When `azcfg.json` exists, `config set project.service|values|secrets|vault.<env>` writes the
  manifest as well as the binding (ADR 0012 Amendment 2); without it the behaviour above is unchanged.
- **New key** `project.shared` — settable only to `true`: writes `azcfg.json` from the current binding. Reading it gives
  `true` (with the manifest path) or `false`. Unsharing = deleting the file.
- The `local-settings` target name becomes accepted once `LocalSettingsTarget` ships (CLI 0.6.0), as planned above.
