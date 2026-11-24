# AzCfg

Local manager for Azure App Configuration and Key Vault for .NET developers: two-way sync of configuration
between Azure and your local files (user-secrets, `appsettings.local.json`, `local.settings.json` for Azure
Functions) and JSON editing of App
Configuration. Ships as the `azcfg` dotnet tool.

- Pull an environment's config (App Configuration keys + Key Vault secrets) into your local files.
- Change values locally, review the diff with `plan`, apply it with `push`.
- Edit a whole environment as one JSON document in your editor.
- Promote new keys from one environment to another.
- Secret values never land in git-tracked files, plans, logs or `--json` output.

## Install

Requires the .NET 10 SDK or runtime.

```bash
dotnet tool install --global AzCfg
azcfg --help
```

Update to the latest version:

```bash
dotnet tool update --global AzCfg
```

Prereleases (e.g. `0.5.0-preview.1`) are installed only on request:

```bash
dotnet tool update --global AzCfg --prerelease
```

Uninstall:

```bash
dotnet tool uninstall --global AzCfg
```

## Authentication

azcfg authenticates only through `DefaultAzureCredential`. The simplest way is the Azure CLI:

```bash
az login
```

### Required Azure RBAC roles

| Resource | Read (`init`, `pull`, `use`, `plan`) | Write (`push`, `edit`, `promote`) |
| --- | --- | --- |
| App Configuration store | App Configuration Data Reader | App Configuration Data Owner |
| Key Vault (secrets) | Key Vault Secrets User | Key Vault Secrets Officer |

`init` also lists App Configuration stores through Azure Resource Graph, so the account needs Reader on the
subscriptions that hold them (or add a store endpoint manually).

## Key conventions

- Service keys: `<service>:<Key:Path>` (e.g. `orders-api:Db:Host`); shared keys: `global:<Key:Path>`.
- `pull` merges `global:*` with `<service>:*`, strips the prefix, and service keys win over global ones.
- `push` writes to the service scope by default; global keys are changed only when they are marked as global
  locally (see [Global keys](#global-keys-in-local-files)) and always ask for confirmation.
- Key Vault references appear as `{ "@kv": "secret-name" }`; adding `"value"` creates or updates the secret.
  Secret names use `--` instead of `:`.

## Workflows

Run commands from your project folder (or pass `--project <PATH>`). Every command accepts `--env <ENV>`,
`--project <PATH>`, `--yes` and `--json`.

### 1. One-time setup

```bash
azcfg init                                   # choose the mode, discover stores/labels, name environments
azcfg config list                            # show the saved settings
azcfg config set env.dev.label Development   # fix a setting without re-running init
```

Settings are per user (not stored in the repo): `mode`, `defaultLabel`, `gitignoreLocalFiles`,
`env.<name>.endpoint`, `env.<name>.label`, `env.<name>.protected`.

Project settings (the binding created by the first `pull`/`use`, also per user) are `project.*` keys, shown by
`config list` inside a bound project:

```bash
azcfg config set project.service orders-api                  # rebind to another service (clears the active env)
azcfg config set project.values appsettings-local            # <target>[:<project path>]
azcfg config set project.secrets user-secrets:src/Api/Api.csproj
azcfg config set project.vault.dev https://my-kv.vault.azure.net/
azcfg config set project.vault.dev ""                        # unset
```

Targets: `user-secrets`, `appsettings-local`, `local-settings` (Azure Functions). Changing a target does not move data: run `azcfg pull --force` to fill
the new one. A secrets target must be git-ignored. `project.active` and `project.ignore` are read-only (use `azcfg use`
and `azcfg ignore`). `pull`/`use --service X` on a project bound to another service fails with the
`config set project.service` command to run.

### 2. Get the config of an environment

```bash
azcfg use dev                     # pull dev into the local targets and make it the active environment
azcfg pull --service orders-api   # first pull when the service cannot be detected from the project
azcfg pull                        # re-pull the active environment (--env <ENV> for another)
azcfg pull --force                # overwrite local changes that were not pushed
azcfg pull --key "Db:*"           # pull only the matching keys of the active environment (repeatable)
azcfg pull --key Db:Host --force  # resolve one conflict by keeping the remote value
```

`use` is safe by default: it lists the blocking keys when local changes would be overwritten, asks before the first
activation overwrites unmanaged local keys (`--yes` skips it) and, for a protected environment, before secrets are
written to local files (`--confirm <ENV>` skips it). With `--json` it never prompts and reports the refusal as data.

On the first pull azcfg binds the project to a service and to local targets: user-secrets (outside the repo)
or `appsettings.local.json` next to the `.csproj`; for Azure Functions projects also `local.settings.json` (see below).
Only application projects are offered as targets — class libraries and test projects are skipped.

### 3. Change config and push it back

```bash
# edit your user-secrets / appsettings.local.json as usual, then:
azcfg plan                 # show what pushing the local files would change
azcfg push                 # add and update changed keys (never deletes)
azcfg push --delete        # also delete remote keys that you deleted locally
azcfg plan --key "Db:*"    # plan/push only the keys matching a pattern (`*`, optional `global:`; repeatable)
azcfg push --key Db:Host --force   # resolve one conflict by keeping the local value
```

Writes use etags, so a key changed remotely since your last pull is reported instead of overwritten.

### 4. Edit an environment as JSON

```bash
azcfg edit --env dev       # opens the remote config in $VISUAL/$EDITOR, shows the plan, then applies it
azcfg edit --set Db:Host=h --unset Old:Key --secret Db:Password=orders--Db--Password   # no editor; repeatable flags
azcfg edit --global --set Logging:Level=Info                                          # keys go to the global scope
```

Secret values are never accepted on the command line; `--secret` only points a key at an existing Key Vault secret.

### 5. Promote keys between environments

```bash
azcfg promote dev stage                       # keys present in dev but missing in stage, via your editor
azcfg promote dev stage --in promote.json     # plan and apply an edited promote file, no editor
```

### 6. Keep some keys local

```bash
azcfg ignore add "global:Logging:*"   # pull keeps them, push skips them
azcfg ignore list
azcfg ignore remove "global:Logging:*"
```

Patterns are stored per user for this project; nothing is deleted anywhere. `*` matches anything including
`:`; the `global:` prefix limits a pattern to global keys (`global:*`, `global:Serilog:*`, `*:Password`,
`ConnectionStrings:Local`).

### 7. Daily workflow: status and diff

```bash
azcfg status                       # active environment, local/remote changes, conflicts, blocked keys, warnings
azcfg status --env stage           # the same for another environment
azcfg diff                         # local files <-> active environment
azcfg diff stage                   # local files <-> stage
azcfg diff dev stage               # dev <-> stage (either side may be `local`)
azcfg diff --same                  # also show equal keys
azcfg diff --missing --all         # keys missing in some environments; exit code 1 if any (usable in CI)
```

Secret values are never shown: only their names and whether they are the same or different.

```bash
azcfg status --check               # configuration health; exit code 1 on errors (usable in CI)
```

`status --check` verifies the Azure sign-in, access to every environment, that the secrets target is git-ignored
(and warns about the values target), Key Vault references that point to missing secrets, keys missing in some
environments and a binding that no longer matches reality. Each finding comes with the command that fixes it.
Secrets are checked by metadata only; their values are never read.

Two more checks cover the team setup: **ManifestInvalid** (error) when `azcfg.json` is unreadable, breaks the schema or
needs a newer azcfg, and **SecretLikeKeyInTrackedFile** (warning) when a git-tracked `appsettings*.json` has a value
under a key that looks secret (`password`, `secret`, `token`, `apikey`, `connectionstring`, or a key under a section such
as `Secrets`). The finding names the file and the key, never the value; the check is skipped outside git.

### 8. Protected environments

Applying to a protected environment (by default `prod`, `production`, `prd`) requires typing its name, or
`--confirm <ENV>` in scripts. `--yes` skips other confirmation prompts.

```bash
azcfg push --env prod --confirm prod
```

## Environment modes

`azcfg init` asks how environments are separated (`--mode stores|labels`):

- **One store per environment** — labels never select the environment: every label in the store is read, so a
  `dev` environment works even when its keys carry the label `Development`. If the same key exists under
  several labels, one copy is used (the default label for new keys, else the unlabeled one) and a warning is
  shown. New keys get the default label (`--label`, or `azcfg config set defaultLabel <label>`).
- **One store, a label per environment** — `init` asks the label of each environment (defaults to the label
  chosen from the store, or the environment name). Unlabeled keys are read as shared defaults and the
  environment's label wins. Writes always carry the environment's label; a key that exists only under another
  label (or none) is never changed — azcfg adds an override with the environment's label and warns.

## Global keys in local files

Local JSON files are written indented. Keys that come from `global:` are marked with a `//@global` comment at
the end of the line; `push` sends marked keys to the `global` scope. Add or remove the marker by hand to move a
key between scopes:

```json
{
  "Logging": {
    "LogLevel": "Information" //@global
  },
  "Api": {
    "Url": "https://example.org", //@global
    "Timeout": "30"
  }
}
```

`dotnet user-secrets set` rewrites the file without comments, so the markers are lost there (the keys become
service keys).

## Azure Functions: `local.settings.json`

For a Functions project (`<AzureFunctionsVersion>` in the `.csproj`) azcfg offers `local.settings.json` next to the
`.csproj` as a target (`local-settings`). Only the `Values` section is read and written, as a flat dictionary
(`Api:Url` stays one key, no nesting); `IsEncrypted`, `Host` and `ConnectionStrings` are kept as they are, and a
missing file is created. Secrets are written there only when git ignores the file. `//@global` markers are not used in
this file, so push treats all its keys as service keys.

```bash
azcfg config set project.values local-settings
azcfg config set project.secrets local-settings:src/Functions/Functions.csproj
```

## Sharing the project with the team: `azcfg.json`

`azcfg.json` is a small, git-tracked file that holds the shared half of a project's binding, so a new teammate runs
`azcfg pull` without the first-run wizard. It contains names and paths only, never a value (plain or secret):

```json
{
  "schemaVersion": 1,
  "service": "orders-api",
  "values":  { "target": "appsettings-local", "project": "src/Api" },
  "secrets": { "target": "user-secrets",      "project": "src/Api/Api.csproj" },
  "vaults":  { "dev": "https://my-kv.vault.azure.net" }
}
```

- The schema is strict: an unknown field is an error (a `value` or `password` field is rejected by name), `service` is
  not empty, has no `:` and is not `global`, `target` is `user-secrets`, `appsettings-local` or `local-settings`,
  `project` is a path relative to the manifest folder that stays inside it, vault URIs are absolute https. `vaults`
  is optional. Comments and trailing commas are allowed. A file with a newer `schemaVersion` asks you to update azcfg.
- Lookup: the nearest `azcfg.json` from the project folder upward, up to the git root.
- Shared (in the file): service, values/secrets targets, per-environment vaults. Per user (not in the file): the
  active environment, ignore patterns, snapshots and the environment profile (`azcfg init`).
- A project without a saved binding but with a manifest is bound from the manifest, no wizard. A manifest with another
  service than the saved binding follows the `config set project.service` rule (the active environment and snapshots
  are reset).

```bash
azcfg config set project.shared true   # write azcfg.json from the current binding (only `true` is accepted)
azcfg config get project.shared        # true + the manifest path, or false
```

The first interactive `pull`/`use` also asks "Save as azcfg.json for the team?" (default no; `--yes` and `--json` never
write it). Once the project is shared, `config set project.service|values|secrets|vault.<env>` update `azcfg.json` as
well and print "Shared via azcfg.json - commit it". Commit the file.

## Keeping config in the project folder

```bash
azcfg config set gitignoreLocalFiles true
```

With this setting `appsettings.local.json` next to the `.csproj` becomes the preferred target (also for
secrets): when the project is bound, azcfg adds the file to the project's `.gitignore` (unless git already
ignores it). Secrets are written there only after
`git check-ignore` confirms the file is ignored. Off by default.

## JSON output

`--json` writes the result to stdout as JSON, without prompts or decoration; errors are also reported as JSON.
`status --json` and `diff --json` print the record as-is. `init --json` and `edit --json` only print (candidates / the exported config) and change nothing.

```bash
azcfg plan --json
```

## UI (preview)

`azcfg ui` starts a local web UI on `127.0.0.1` and opens it in the browser with a one-time token (the server is not
reachable from other machines). It uses the same sign-in (`az login`), RBAC roles and user state as the CLI.

```bash
azcfg ui                 # real Azure, random port, opens the browser
azcfg ui --port 5190 --no-browser
azcfg ui --demo          # in-memory demo world, no Azure needed (--demo=empty: no seed)
azcfg ui --json          # prints { "url": "..." } and keeps running until Ctrl+C
```

Open a folder with your solution, bind each project (per `.csproj` folder) to its local targets, then work in three
modes per project tab: **Browse** (local vs remote values, edits go to a draft), **Compare** (environment ↔ environment,
local ↔ environment, consistency matrix) and **Sync** (pull / push with a plan and typed confirmation for protected
environments). The **Problems** panel shows the `azcfg status --check` report.

**Share with team** in the project settings writes `azcfg.json` (same as `config set project.shared true`); a shared
project carries a "shared (azcfg.json)" badge, editing its service or targets tells you that `azcfg.json` changed
("commit it"), and a project with a manifest opens as bound even with an empty user state.

## Build from source

```bash
dotnet build AzCfg.slnx
dotnet test AzCfg.slnx
dotnet pack src/AzCfg.Cli -c Release -o artifacts
dotnet tool install --global --add-source artifacts AzCfg
```

Architecture, decisions and the roadmap: [`docs/architecture.md`](docs/architecture.md), [`docs/adr/`](docs/adr).

## Release

The CLI is published by `.github/workflows/publish-cli.yml` on every push to `master` that changes `src/**`
or `Directory.*.props`. Bump `<Version>` in `src/AzCfg.Cli/AzCfg.Cli.csproj` before merging to `master`:
nuget.org does not accept an already published version; the push uses `--skip-duplicate`, so an unbumped version is
skipped instead of failing. A SemVer suffix (`0.5.0-preview.1`) publishes a prerelease that `dotnet tool update`
ignores unless `--prerelease` is passed; drop the suffix to release it as stable.

The workflow runs the tests, packs `AzCfg` and pushes it to nuget.org. It authenticates via Trusted Publishing
(OIDC) and requires the `NUGET_USER` repository secret (nuget.org profile name).
