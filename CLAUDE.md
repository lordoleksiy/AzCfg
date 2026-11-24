# AzCfg

Local manager for Azure App Configuration + Key Vault for .NET developers: two-way sync of config between Azure
and local files (user-secrets, `appsettings.local.json`, `local.settings.json` for Azure Functions)
and JSON editing of App Configuration.
Core is a class library; the client is currently a CLI (`dotnet tool azcfg`), a UI comes later.
Code, identifiers, commits and docs are in English (existing ADRs and plans in `docs/` are in Ukrainian).

## Commands
```
dotnet build AzCfg.slnx
dotnet test AzCfg.slnx
dotnet test tests/AzCfg.Core.Tests --filter "FullyQualifiedName~KeyMapper"
dotnet format AzCfg.slnx
dotnet run --project src/AzCfg.Cli -- <args>
```
Warnings = errors (`Directory.Build.props`). Package versions only in `Directory.Packages.props`.

## Structure and layer boundaries
```
src/AzCfg.Core            model, Mapping, Environments, Sync, Security, Abstractions (ports), UseCases
src/AzCfg.Infrastructure  Azure SDK, file system, user state, DI (AddAzCfg)
src/AzCfg.Cli             Spectre.Console.Cli — user interaction only; command tree in CliApp.Configure
src/AzCfg.DevConsole      debug harness: runs CLI commands in-process (F5 + breakpoints)
tests/AzCfg.Core.Tests    the bulk of the tests; hand-written port fakes in Fakes/
tests/AzCfg.Infrastructure.Tests
tests/AzCfg.Cli.Tests     command tests + Scenario/ (end-to-end on in-memory fakes)
```
- **Core references nothing**: no Azure SDK, no file IO, no console. Only BCL + `ILogger`.
- **Core never prompts or prints** — it returns data (`SyncPlan`, `ProjectResolution`); the client decides.
- Infrastructure implements the ports from `Core/Abstractions`; Cli depends on Core and Infrastructure.
- Everything the CLI prints must have a `--json` equivalent (the use-case record serialized as-is).
- One type per file, file-scoped namespaces.

## Key conventions
- Service: `<service>:<Key:Path>`; global: `global:<Key:Path>`.
- Pull: `global:*` + `<service>:*`, the prefix is stripped, service keys override global.
- Push by default touches only the service scope; global only explicitly via `@global`.
- KV secrets: `:` → `--`. The format is a property of the local target (`LocalTargetBinding.Format`): `NestedJson`
  (path segments split on `:`, no scope prefix) for user-secrets/`appsettings.local.json`; `local.settings.json`
  — only `FlatDictionary` (flat `Values` dictionary, `A:B` keys as-is, no nesting).
- `{ "@kv": "name" }` → KV reference; with `"value"` → create/update the secret.
- Labels (ADR 0008): store mode reads all labels (duplicates → one copy + `SyncWarning`); label mode — the
  environment's label on top of unlabeled keys; Core sets each entry's label, Infrastructure writes `entry.Label`.
- Push is upsert by default (no deletes); `--delete` adds deletes. Ignore rules — `ProjectBinding.Ignore`.
- Global keys in local JSON files are marked with `//@global` at the end of the line (ADR 0009); push assigns them to `global`.

## Security
- Auth only via `DefaultAzureCredential`.
- Secrets are never written to git-tracked files.
- Writes to App Config use `If-Match: etag`; prod apply requires typing the environment name.
- Secret values never end up in snapshots, plans, logs or `--json` output —
  only via `SecretString` (redacted to `"***"`) and a fingerprint (`ISecretFingerprinter`).

## Working rules
- TDD for Core: failing test first, then code.
- Small steps: one entity/behavior at a time; after each step `dotnet build` + `dotnet test`.
- No business logic beyond the current MVP step; do not refactor others' code unasked.
- New dependencies only via `Directory.Packages.props` and only when needed.
- Changelog: every user-visible change updates `CHANGELOG.md` (git) and the version/release notes in the Cli csproj (NuGet).
- Model: sealed records, immutable; async methods take a `CancellationToken`.

## Documentation
- Full architecture, models, ports, CLI: `docs/architecture.md`
- CLI plan: `docs/mvp-plan.md` (stages 1–9 done, CLI 0.1.0–0.5.0; 0.5.1 = `local.settings.json`; reorder 2026-11-21:
  0.6.0 = UI (`azcfg ui`), 0.7.0 = stage 10 manifest for CLI + UI (done); orchestrator sessions and branching; where it
  differs from later ADRs, the ADR wins, except release numbers — `docs/cli-roadmap.md` §8.1)
- Branching: one trunk `master` — `feature/<id>-<slug>` from `origin/master`, PR into `master`; a release is a PR that
  changes `<Version>` (NuGet publishes only then, `--skip-duplicate`). No `dev`/`release/*` branches after session 0.
- CLI roadmap analysis (command budget, why this order, 2026-11-21 scope revision §8): `docs/cli-roadmap.md`
- UI plan (tasks for the orchestrator): `docs/ui-mvp-plan.md`
- User-facing CLI reference, RBAC roles, release: `README.md`
- Decisions (ADR): `docs/adr/` (0001 zero-config, 0002 stores per env, 0003 core never prompts,
  0004 key naming, 0005 KV discovery, 0006 local targets and binding, 0007 sync state and secret safety,
  0008 environment modes / labels, local ignore, push modes, 0009 UX: env label, real targets, `//@global`, gitignore,
  0012 project configuration (amendment 2: minimal `azcfg.json` read by `pull`; adopt/discovery/profiles deferred),
  0013 CLI daily workflow: status, diff, key selection, safe use,
  0014 editable project settings `config project.*`; remote providers / App Service settings deferred to stage 11,
  0016 UI distribution: `azcfg ui` launcher in the CLI tool, Web SDK + static web assets, pack smoke in CI)

## Skills
- `/feature` — one task → branch from `origin/master` → tests → code → PR into `master`
- `/add-local-target` — new `ILocalTarget` + test + DI
- `/add-cli-command` — new command + `--json`
- `/adr` — new ADR as a contract before code
- `/issue-orchestrator` — issue → ADR → worktree agents → merge → e2e check
- `/cli-scenario-test` — end-to-end CLI scenario on in-memory fakes (init → pull → ignore → push)
