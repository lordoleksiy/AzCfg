# Changelog

All notable changes to the `AzCfg` CLI (dotnet tool `azcfg`). Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versions follow [SemVer](https://semver.org/). Update this file in the same PR as every user-visible change and when
bumping `<Version>` in `src/AzCfg.Cli/AzCfg.Cli.csproj`; the NuGet release notes are taken from the same entry.

## [Unreleased]

### Planned
- 0.5.0: `azcfg status --check` (sign-in, access, `.gitignore`, dead Key Vault references, keys missing across environments).
- 0.6.0 (ADR 0012, amended): project manifest `azcfg.json`, discovery, `azcfg adopt`.

## [0.4.0]
### Added
- `azcfg status`: active environment, local/remote change counters, conflicts, blocked keys, warnings (ADR 0013).
- `azcfg diff`: local ↔ env, env ↔ env, `--same`, `--missing --all` (exit code 1 when keys are missing).
- `--key <pattern>` for `pull`/`plan`/`push`; per-key conflict resolution with `pull --key K --force` (keep remote)
  and `push --key K --force` (keep local).
- `edit --set/--unset/--secret` (with `--global`) patch the config without an editor.
### Changed
- `use` lists blocking keys, asks before overwriting unmanaged local keys on first activation (`--yes`) and before
  writing a protected environment's secrets locally (`--confirm <ENV>`).
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
