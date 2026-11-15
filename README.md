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
azcfg init                       # discover stores, name environments, mark protected ones
azcfg use dev                    # pull dev into the local targets and make it active
azcfg pull                       # re-pull the active environment (--env <ENV> for another)
azcfg pull --force               # overwrite local changes that were not pushed
azcfg plan                       # show what pushing the local files would change
azcfg push                       # apply the local files to the active environment
azcfg edit --env dev             # edit the remote config as JSON in $VISUAL/$EDITOR, then apply
azcfg promote dev stage          # add keys present in dev but missing in stage, via your editor
azcfg promote dev stage --in promote.json   # plan and apply an edited promote file, no editor
```

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
