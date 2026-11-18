# AzCfg

Local manager for Azure App Configuration and Key Vault for .NET developers: two-way sync of configuration
between Azure and your local files (user-secrets, `appsettings.local.json`) and JSON editing of App
Configuration. Ships as the `azcfg` dotnet tool.

- Pull an environment's config (App Configuration keys + Key Vault secrets) into your local files.
- Change values locally, review the diff with `plan`, apply it with `push`.
- Edit a whole environment as one JSON document in your editor.
- Promote new keys from one environment to another.
- Secret values never land in git-tracked files, plans, logs or `--json` output.

## Install

Requires the .NET 10 SDK or runtime.

```bash
dotnet tool install --global AzCfg --version 0.3.1
azcfg --help
```

Update to the latest version:

```bash
dotnet tool update --global AzCfg
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

### 2. Get the config of an environment

```bash
azcfg use dev                     # pull dev into the local targets and make it the active environment
azcfg pull --service orders-api   # first pull when the service cannot be detected from the project
azcfg pull                        # re-pull the active environment (--env <ENV> for another)
azcfg pull --force                # overwrite local changes that were not pushed
```

On the first pull azcfg binds the project to a service and to local targets: user-secrets (outside the repo)
or `appsettings.local.json` next to the `.csproj`. Only application projects are offered as targets — class
libraries and test projects are skipped.

### 3. Change config and push it back

```bash
# edit your user-secrets / appsettings.local.json as usual, then:
azcfg plan                 # show what pushing the local files would change
azcfg push                 # add and update changed keys (never deletes)
azcfg push --delete        # also delete remote keys that you deleted locally
```

Writes use etags, so a key changed remotely since your last pull is reported instead of overwritten.

### 4. Edit an environment as JSON

```bash
azcfg edit --env dev       # opens the remote config in $VISUAL/$EDITOR, shows the plan, then applies it
```

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

### 7. Protected environments

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
`init --json` and `edit --json` only print (candidates / the exported config) and change nothing.

```bash
azcfg plan --json
```

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
nuget.org does not accept an already published version, so the run fails if it was not bumped.

The workflow runs the tests, packs `AzCfg` and pushes it to nuget.org. It authenticates via Trusted Publishing
(OIDC) and requires the `NUGET_USER` repository secret (nuget.org profile name).
