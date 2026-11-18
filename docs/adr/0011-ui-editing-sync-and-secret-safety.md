# 0011. UI editing, sync and secret safety

Status: accepted (UI MVP, task D1; plan `docs/ui-mvp-plan.md` §5.3, §7.1)

## Context
- The UI (ADR 0010) edits Azure configuration in a tree, an inspector and a JSON editor, edits local files, compares
  environments and pulls/pushes selectively. The CLI flows (`edit`, `pull`, `push`, ADR 0007/0008) are all-or-nothing
  and short-lived; a UI edit can stay open for a long time.
- Gaps in Core for these flows:
  - pull is all-or-nothing (G6); a push plan cannot be partially selected (G7);
  - `PushSource.EditedJson`/`Promote` `ToString()` prints JSON that may hold secret values (G8);
  - `EditedJson` uses the current remote as the 3-way base, so a long-open edit silently reverts others' changes (G14);
  - no operation resolves a local-files conflict (G25);
  - remote-only browse and environment ↔ environment compare need entries without resolving secrets (G27);
  - a UI apply gate would duplicate Core rules (G28); the secret-name rule of `ThreeWayDiff` is internal (G24).
- Local values returned by Core are plain strings: whether a local value is a secret depends on which target it came
  from.
- Protected environments (ADR 0007) are confirmed by typing the name in the CLI; the UI must make the same risk visible
  and keep Core as the final check.

## Decision
### AD-5 Edit model
- **Azure edits** (tree, inspector, JSON) go into a `RemoteDraft` per (project, environment):
  - `BaseJson` = the edit JSON exported when the draft started;
  - `Json` = the edited edit JSON **without** secret values;
  - `Secrets` = typed secret values (`SecretString`).
- Review = `PlanPushAsync(EditedJson(Json + injected secrets, BaseJson))`. Core does a 3-way merge against `BaseJson`,
  so concurrent Azure changes of untouched keys are never reverted and same-key edits become conflicts (G14). Drafts
  always carry `BaseJson`.
- **Local edits** (inspector) are immediate writes through `ILocalTarget` (`ILocalValuesService`), followed by a normal
  push.

### AD-6 Secrets
- Existing: `SecretString`, fingerprints, plans and snapshots without values (ADR 0007).
- New rules:
  1. A secret value is never rendered before Reveal.
  2. Reveal of a remote secret = `ISecretStore.GetAsync`; the value lives only in a `SecretValue` component field and is
     hidden after 30 s, on tab change and on `Dispose`.
  3. A key is secret when its remote/base side is a secret **or** its local value comes from `SecretsTarget` while that
     differs from `ValuesTarget` (`SecretPolicy.IsSecret`). This rule is mandatory everywhere a local value is shown.
  4. Search, palette and filters never index secret values.
  5. Logs contain only keys and operation titles.
  6. A typed secret value goes straight into `SecretString`, reaches Core only via `"value"` of edit JSON built in
     `DraftService.ToPushSource`, and is dropped after apply or discard.
  7. JSON shown in Monaco never contains secret values (`{"@kv": name}` or `***`).

### AD-7 Protected environment
- Existing in Core: `IsProtected`, `ConfirmedEnvironment`, `ConfirmGlobal`.
- New in the UI: red frame + PROTECTED badge + lock; **read-only by default** — edit actions are disabled until
  "Unlock editing" (`ProtectedUnlockState`, per circuit and environment); apply needs a typed confirmation (button
  disabled until a case-insensitive match, no default Enter); pull from a protected environment shows a separate
  warning.
- UI checks are an extra layer; Core makes the final check.

### AD-8 Active environment
- Push of local files and Pull preview only for the **active** environment (`InvalidOperationException` in Application
  otherwise).
- Browse of a non-active environment is remote-only.
- "Make active" = the `use` flow.
- Drafts and Compare work for any environment.

### Core semantics (additive; contracts in `docs/ui-mvp-plan.md` §7.1)
Core changes are additive and TDD; existing CLI behaviour does not change.

**Edited JSON with a base (G14, G8).**
- `PushSource.EditedJson(string Json, string? BaseJson = null)`. With `BaseJson`, `PlanPushAsync` uses it (parsed with
  `FromEditJson` and the known secrets) as the 3-way base instead of the current remote: Azure changes made after the
  export are not reverted; keys changed on both sides become Conflicts. Without `BaseJson` the behaviour is unchanged
  (base = current remote).
- `EditedJson` and `Promote` print `Json = ***` (+ `BaseJson = ***` or `BaseJson = null`; `Promote` keeps
  `FromEnvironment = <name>`) from `ToString()`.

**Edit-JSON helpers (G24).**
- `KeyMapper.SecretNameOf(Uri)` — the segment after `secrets/` (a version segment is ignored); no `secrets/<name>`
  segment → `ArgumentException`. `ThreeWayDiff` delegates to it.
- `EditJsonPatch` — pure edits of edit-JSON text addressed by `ScopedKey(Scope, Key)` (`SetPlain`,
  `SetSecretReference`, `SetSecretValue`, `Remove`, `CopyKey`, `Get`, `ChangedKeys`). Invalid JSON → `JsonException`;
  a path through a leaf or a leaf/object clash → `ArgumentException` naming the key; messages never contain values.
  The result of `SetSecretValue` contains the secret: it is passed straight to Core and never stored, logged or
  rendered.

**Selective push (G7).** `PlanSelection.Select(plan, keys)` restricts a plan to `keys` (ordinal): changes whose
`(After ?? Before).Key` is in `keys`, conflicts and warnings whose `Key` is in `keys`. A `CreateSecret` awaiting a vault
(`Before == null`, `After == null`) is kept regardless of `keys`. `TouchesGlobal` and `RequiresVaultChoice` are
recomputed; `Binding` and `Environment` are unchanged. Applying the result updates the snapshot only for the selected
keys.

**Apply gate (G28).** `ApplyGuard.Check(plan, options)` returns every reason `ApplyAsync` refuses a plan, in the order
`Conflicts`, `VaultChoiceRequired`, `EnvironmentNotConfirmed`, `GlobalNotConfirmed`; empty → apply proceeds.
`SyncService.ApplyAsync` calls it and throws `InvalidOperationException` with today's message of the first blocker. The
UI uses the same guard to enable or explain the Apply action.

**Selective pull (G6).** `ISyncService.PullSelectedAsync(binding, env, keys)` pulls only `keys`, keeping every other
local value.
- `env` must equal `binding.ActiveEnvironment` (OrdinalIgnoreCase), else `InvalidOperationException` (also when there
  is no active environment).
- Before any write: the same gitignore and secrets-target safety steps as `PullAsync`.
- A selected key: remote plain → `ValuesTarget` (removed from `SecretsTarget`); KV reference → secret value to
  `SecretsTarget` (removed from `ValuesTarget`); missing remotely → removed locally; the scope marker follows the
  remote entry. Ignored keys are skipped and listed in `Ignored`.
- The snapshot is updated only for `keys` (secrets as fingerprint). Returns counts; `Blocked = false`,
  `LocalChanges = []`.

**Conflict resolution (G25).** `ISyncService.ResolveConflictAsync(binding, env, key, resolution)` — active environment
only, same rule as `PullSelectedAsync`.
- `KeepRemote` = `PullSelectedAsync([key])`.
- `KeepLocal` = the snapshot item of `key` becomes the current remote item (removed when missing remotely); local files
  are untouched, so the next `PlanPushAsync(LocalFiles)` plans the local value (Add/Update; a local deletion is a
  Delete only with `LocalFiles.Delete`, else `DeleteSkipped`).

**Remote read without secrets (G27).** `ISyncService.ReadRemoteConfigAsync(binding, env)` returns `RemoteConfig`: the
effective remote configuration of `binding.Service` in `env` after `LabelSelector` (ADR 0008) and `ScopeMerger` (one
entry per key, service overrides global), labels and ETags kept; `Shadowed` = global entries hidden by a service
override; `Warnings`. It never calls `ISecretStore`.

**Environment comparison (G27).** `EnvironmentComparer.Compare(left, right)` → `ComparisonItem(Key, Kind, Left, Right)`
with `ComparisonKind { Same, Different, OnlyLeft, OnlyRight }`, matched and ordered by `Key` (ordinal).
Plain ↔ plain: ordinal value equality. Reference ↔ reference (or resolved secret, value ignored): equal
`KeyMapper.SecretNameOf` (vault hosts may differ). Plain ↔ reference: `Different`. Scope and label do not affect
`Kind`. A duplicate key within one side → `ArgumentException` naming the key. Secret values are never involved.

## Consequences
- A long-open Azure draft never reverts changes others made in the meantime; overlapping edits surface as conflicts in
  the review.
- The UI can pull, push and resolve conflicts per key without re-implementing Core rules; the CLI keeps its behaviour.
- Apply is refused by Core for exactly the reasons the UI shows.
- Secret values exist in the UI process only as `SecretString`, in a revealed `SecretValue` for at most 30 s, or inside
  the push source handed to Core; they never appear in markup before Reveal, Monaco, search, logs or `ToString()`.
- Production and other protected environments are read-only in the UI until explicitly unlocked, and apply still needs
  the typed name.
- Local-file operations are tied to the active environment; other environments are remote-only in Browse.
