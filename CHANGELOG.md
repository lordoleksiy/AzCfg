# Changelog

All notable changes to the `AzCfg` CLI (dotnet tool `azcfg`). Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versions follow [SemVer](https://semver.org/). Update this file in the same PR as every user-visible change and when
bumping `<Version>` in `src/AzCfg.Cli/AzCfg.Cli.csproj`; the NuGet release notes are taken from the same entry.

## [Unreleased]

### Planned
- 0.7.0 (ADR 0012, amendment 2): minimal project manifest `azcfg.json`, `project.shared`, manifest and secret-like-key
  checks in `status --check`; "share with team" in the UI.
- 0.8.0 (ADR 0014): App Service / Function App settings as remotes (`--remote`, `env@remote`, `promote --key`).
- 0.9.0 (ADR 0015): manifest v2 — team environments, key layout, custom JSON target file.

## [0.6.0]
### Added
- `azcfg ui` — preview: a local web UI (Blazor Server on `127.0.0.1`, one-time browser token) for browsing, comparing
  and syncing the configuration of a workspace. Options: `--port N`, `--no-browser`, `--demo[=empty]` (in-memory demo
  world, no Azure), `--json` (prints `{ "url": … }` and keeps running). Same user state and auth as the CLI (ADR 0016).
- UI: Problems panel — the `status --check` report in the sidebar and the status bar, with actions per finding.
- UI: consistency matrix in Compare (keys × all environments, gaps only) and "Copy from ‹env›" / "Copy to right" into the
  target environment's draft; secret values are never copied, a new value is typed instead.
- UI: `local.settings.json` (Azure Functions) is a writable values/secrets target, as in the CLI since 0.5.1.

## [0.5.1]
### Added
- Azure Functions `local.settings.json` as a local target (`local-settings`), offered for Functions projects
  (`<AzureFunctionsVersion>`): only the `Values` section is read and written, as a flat dictionary with `A:B` keys;
  other sections (`IsEncrypted`, `Host`, `ConnectionStrings`) are kept; a missing file is created. Secrets are written
  there only when the file is git-ignored, as for `appsettings.local.json`. No `//@global` markers in this file: all
  its keys are service keys. `azcfg config set project.values|secrets local-settings` accepts it.

## [0.5.0]
Stable release of everything in `0.5.0-preview.1` below, plus:
### Changed
- `status --check`: each environment in `--json` gains `access` (`ok`, `authRequired`, `forbidden`, `unreachable`);
  `reachable` stays. A sign-in, 403 or network failure is described with the fix (`az login`, the required role)
  instead of the exception type name.

## [0.5.0-preview.1]
Prerelease: install with `--prerelease` (or `--version 0.5.0-preview.1`). Not yet verified against real Azure.
### Added
- `azcfg status --check`: Azure sign-in, access to each environment, git-ignored secrets/values targets, Key Vault
  references to missing secrets (metadata only, values are never read), keys missing in some environments, binding
  mismatches; each finding has a fix command. Exit code 1 on errors; `--json` prints the report (ADR 0013 amendment).
- Editable project settings: `azcfg config list|get|set project.*` (`service`, `values`, `secrets`, `vault.<env>`;
  `active`, `ignore` read-only). Changing the service clears the active environment and its snapshots (ADR 0014).
### Changed
- `status --check`: local target checks (git-ignored secrets/values, missing target projects) also run when not signed in
  to Azure; a Key Vault secret that is disabled counts as missing; the fix for a missing global key is `azcfg edit --global`.
- `plan`/`push` warn (human output only) when no environment has been pulled, e.g. right after changing the service.
- `project.vault.<env>` accepts only a root https URI; `--service` is compared case-sensitively.
- `pull`/`use --service X` on a project bound to another service fails with the `config set project.service` command
  instead of silently ignoring `--service`.
- CI runs build, tests and `dotnet format` on pull requests; NuGet publishing skips an already published version.

## [0.4.0]
### Added
- `azcfg status`: active environment, local/remote change counters, conflicts, blocked keys, warnings (ADR 0013).
- `azcfg diff`: local ↔ env, env ↔ env, `--same`, `--missing --all` (exit code 1 when keys are missing). Local
  secrets (user-secrets, or secrets of the active environment) stay hidden even against an environment without them.
- `--key <pattern>` for `pull`/`plan`/`push`; per-key conflict resolution with `pull --key K --force` (keep remote)
  and `push --key K --force` (keep local; nothing is recorded until the push is applied). `pull --key` leaves keys
  that were added locally and never pushed; blank patterns and a bare `global:` are rejected.
- `edit --set/--unset/--secret` (with `--global`) patch the config without an editor; `--set` on a global key adds a
  service override (`--global --set` on a service key sets the global value).
### Changed
- `use` lists blocking keys, asks before overwriting unmanaged local keys on first activation (`--yes`) and before
  writing a protected environment's secrets locally (`--confirm <ENV>`). When the active environment cannot be read
  to check for local changes, `use` asks before switching (`--json`: refused as `unverified`; `--force` skips the check).
- `edit` no longer reverts remote changes made after the export.

## [0.3.1]
### Fixed
- Any `appsettings*.json` is detected and local files are matched case-insensitively.

## [0.3.0]
### Added
- Label per environment prompt in `init`, `//@global` markers in local files, indented JSON output,
  `gitignoreLocalFiles` setting (ADR 0009).
### Changed
- Only real local targets are offered (class libraries and test projects are skipped).

## [0.2.0]
### Added
- Environment modes (store per environment / label per environment), local ignore (`azcfg ignore`), `--delete` for push,
  `azcfg config`, `--service` for `pull`/`use` (ADR 0008).
### Changed
- `push` is upsert by default (no remote deletes).

## [0.1.0]
### Added
- Initial release: `init`, `pull`, `use`, `plan`, `push`, `edit`, `promote`, `--json`.
