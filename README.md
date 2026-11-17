# AzCfg

Local manager for Azure App Configuration and Key Vault: two-way sync of configs between Azure and local
files (user-secrets, `appsettings.local.json`) and JSON editing of App Configuration. Ships as the
`azcfg` dotnet tool.

## Install

Requires the .NET 10 SDK or runtime.

```
dotnet tool install --global AzCfg
azcfg --help
```

Update to the latest version:

```
dotnet tool update --global AzCfg
```

## Authentication

azcfg authenticates only through `DefaultAzureCredential`. The simplest way is the Azure CLI:

```
az login
```

## Required Azure RBAC roles

| Resource | Read (`init`, `pull`, `use`, `plan`) | Write (`push`, `edit`, `promote`) |
| --- | --- | --- |
| App Configuration store | App Configuration Data Reader | App Configuration Data Owner |
| Key Vault (secrets) | Key Vault Secrets User | Key Vault Secrets Officer |

`init` also lists App Configuration stores through Azure Resource Graph, so the account needs Reader on the
subscriptions that hold them (or add a store endpoint manually).

## Usage

Run commands from your project folder (or pass `--project <PATH>`).

```
azcfg init                       # choose the mode, discover stores/labels, name environments
azcfg config list                # show the saved settings (mode, default label, environments)
azcfg config set env.dev.label Development   # fix a setting without re-running init
azcfg use dev                    # pull dev into the local targets and make it active
azcfg pull --service orders-api  # first pull when the service cannot be detected
azcfg pull                       # re-pull the active environment (--env <ENV> for another)
azcfg pull --force               # overwrite local changes that were not pushed
azcfg ignore add "global:Logging:*"   # keep matching keys local: pull keeps them, push skips them
azcfg ignore list | remove <PATTERN>
azcfg plan                       # show what pushing the local files would change
azcfg push                       # add and update changed keys (never deletes)
azcfg push --delete              # also delete remote keys that you deleted locally
azcfg edit --env dev             # edit the remote config as JSON in $VISUAL/$EDITOR, then apply
azcfg promote dev stage          # add keys present in dev but missing in stage, via your editor
azcfg promote dev stage --in promote.json   # plan and apply an edited promote file, no editor
```

### Environment modes

`azcfg init` asks how environments are separated (`--mode stores|labels`):

- **One store per environment** — labels never select the environment: every label in the store is read, so a
  `dev` environment works even when its keys carry the label `Development`. If the same key exists under
  several labels, one copy is used (the default label for new keys, else the unlabeled one) and a warning is
  shown. New keys get the default label (`--label`, or `azcfg config set defaultLabel <label>`).
- **One store, a label per environment** — each environment has its label; unlabeled keys are read as shared
  defaults and the environment's label wins. Writes always carry the environment's label; a key that exists
  only under another label (or none) is never changed — azcfg adds an override with the environment's label
  and warns.

### Ignoring keys locally

`azcfg ignore add <PATTERN>` stores a per-user pattern for this project (nothing is deleted anywhere).
`*` matches anything including `:`; the `global:` prefix limits a pattern to global keys
(`global:*`, `global:Serilog:*`, `*:Password`, `ConnectionStrings:Local`). Matching keys belong to your local
files: `pull` keeps their local values and does not bring them back, `plan`/`push` never add, update or
delete them.

Notes:

- Keys follow `<service>:<Key:Path>`; shared ones use `global:<Key:Path>`. Pull merges `global:*` with the
  service keys, service keys win.
- Secrets appear in `edit` and `promote` only as `{ "@kv": "name" }`; secret values never appear in plans,
  logs or `--json` output (they are shown as `***`).
- Applying to a protected environment (by default `prod`, `production`, `prd`) requires typing its name, or
  `--confirm <ENV>`. `--yes` skips other confirmation prompts.
- Every command accepts `--env <ENV>`, `--project <PATH>`, `--yes` and `--json`.

## JSON output

`--json` writes the result to stdout as JSON, without prompts or decoration; errors are also reported as JSON.
`init --json` and `edit --json` only print (candidates / the exported config) and change nothing.

```
azcfg plan --json
```

## Build from source

```
dotnet build AzCfg.slnx
dotnet test AzCfg.slnx
dotnet pack src/AzCfg.Cli -c Release -o artifacts
dotnet tool install --global --add-source artifacts AzCfg
```

## Release

The CLI is published by `.github/workflows/publish-cli.yml` on every push to `master` that changes `src/**`
or `Directory.*.props`. Bump `<Version>` in `src/AzCfg.Cli/AzCfg.Cli.csproj` before merging to `master`:
nuget.org does not accept an already published version, so the run fails if it was not bumped.

The workflow runs the tests, packs `AzCfg` and pushes it to nuget.org.
It authenticates with nuget.org via Trusted Publishing (OIDC) and requires the `NUGET_USER` repository secret (nuget.org profile name).
