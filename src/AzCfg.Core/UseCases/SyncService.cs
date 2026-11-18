using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.UseCases;

/// <summary>
/// Pull overwrites the local targets with the merged remote config (values to <c>ValuesTarget</c>, resolved Key Vault
/// secrets to <c>SecretsTarget</c>; one write when they are the same target). Unless forced, returns a blocked result
/// (keys only, nothing written) when local targets differ from the active environment's snapshot. Throws <see cref="InvalidOperationException"/>
/// before writing anything when the secrets target is not safe for secrets. The snapshot keeps only secret fingerprints.
/// Keys matched by <see cref="ProjectBinding.Ignore"/> (scope = provenance: remote, else snapshot, else local service; ADR 0008)
/// belong to the local files: pull keeps their local values in the target they were read from, never writes their remote
/// values, leaves them out of the snapshot and never blocks on them; <see cref="PushSource.LocalFiles"/> plans and diffs skip them.
/// </summary>
public sealed class SyncService(
    IConfigStore store,
    ISecretStore secretStore,
    IUserStateStore state,
    ISecretFingerprinter fingerprinter,
    IEnumerable<ILocalTarget> targets) : ISyncService
{
    public async Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, bool force, CancellationToken ct)
    {
        var secretsTarget = targets.First(t => t.Kind == binding.SecretsTarget.Kind);
        if (!(await secretsTarget.InspectAsync(binding.SecretsTarget, ct)).IsSafeForSecrets)
        {
            throw new InvalidOperationException($"{binding.SecretsTarget.Kind} target is not safe for secrets.");
        }

        var (raw, warnings) = await ReadEntriesAsync(env, binding.Service, ct);
        var entries = ScopeMerger.Merge(raw);
        var remoteScopes = entries.ToDictionary(e => e.Key, e => e.Scope, StringComparer.Ordinal);
        var snapshot = binding.ActiveEnvironment is { } active ? await state.LoadSnapshotAsync(binding.RepoPath, active, ct) : null;
        var ignore = binding.Ignore is { Count: > 0 };
        var local = (!force && snapshot is not null) || ignore ? await ReadTargetsAsync(binding, ct) : ([], []);

        if (!force && snapshot is not null)
        {
            var changed = ThreeWayDiff.Compute(snapshot, ToState([.. local.Values, .. local.Secrets]), snapshot, fingerprinter).Items
                .Where(i => i.Kind is DiffKind.LocalAdded or DiffKind.LocalModified or DiffKind.LocalDeleted && !Ignored(i.Key))
                .Select(i => i.Key).ToList();
            if (changed.Count > 0)
            {
                return new(env.Name, 0, 0, true, changed);
            }
        }

        List<LocalValue> values = [], secrets = [];
        List<string> ignored = [];
        var items = new Dictionary<string, StateItem>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (ignore && Ignored(entry.Key))
            {
                ignored.Add(entry.Key);
                continue;
            }

            switch (entry.Value)
            {
                case ConfigValue.Plain plain:
                    values.Add(new(entry.Key, plain.Value, entry.Scope));
                    items[entry.Key] = new(entry.Key, entry.Scope, new StateValue.Plain(plain.Value));
                    break;
                case ConfigValue.KeyVaultRef reference:
                    var secret = new SecretString(await secretStore.GetAsync(reference.SecretUri, ct));
                    secrets.Add(new(entry.Key, secret.Reveal(), entry.Scope));
                    items[entry.Key] = new(entry.Key, entry.Scope, new StateValue.Secret(reference.SecretUri, fingerprinter.Fingerprint(secret)));
                    break;
            }
        }

        var (valuesWritten, secretsWritten) = (values.Count, secrets.Count);
        if (ignore)
        {
            values.AddRange(local.Values.Where(v => Ignored(v.Key)));
            secrets.AddRange(local.Secrets.Where(v => Ignored(v.Key)));
        }

        if (binding.ValuesTarget == binding.SecretsTarget)
        {
            await secretsTarget.WriteAsync(binding.SecretsTarget, [.. values, .. secrets], ct);
        }
        else
        {
            await targets.First(t => t.Kind == binding.ValuesTarget.Kind).WriteAsync(binding.ValuesTarget, values, ct);
            await secretsTarget.WriteAsync(binding.SecretsTarget, secrets, ct);
        }

        await state.SaveSnapshotAsync(binding.RepoPath, env.Name, new ConfigState(items), ct);
        await state.SaveBindingAsync(binding with { ActiveEnvironment = env.Name }, ct);
        return new(env.Name, valuesWritten, secretsWritten, false, []) { Warnings = warnings, Ignored = ignored.Count == 0 ? Array.Empty<string>() : ignored };

        bool Ignored(string key) => IgnoreRules.IsIgnored(
            binding.Ignore, key, remoteScopes.TryGetValue(key, out var scope) ? scope : snapshot?.Items.GetValueOrDefault(key)?.Scope ?? ConfigScope.Service);
    }

    public async Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) =>
        JsonProjection.ToEditJson(ScopeMerger.Merge((await ReadEntriesAsync(env, binding.Service, ct)).Entries)).ToJsonString(new() { WriteIndented = true });

    public async Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct)
    {
        var existing = (await ReadEntriesAsync(to, binding.Service, ct)).Entries
            .Where(e => e.Scope == ConfigScope.Service).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var added = (await ReadEntriesAsync(from, binding.Service, ct)).Entries
            .Where(e => e.Scope == ConfigScope.Service && !existing.Contains(e.Key));
        return JsonProjection.ToEditJson(added, secretPlaceholders: true).ToJsonString(new() { WriteIndented = true });
    }

    public async Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
    {
        var (@base, remote, _, _) = await ReadRemoteAsync(binding, env, ct);
        return WithoutIgnored(ThreeWayDiff.Compute(@base, await ReadLocalAsync(binding, ct), remote, fingerprinter), binding);
    }

    /// <summary>
    /// Plans local changes against <paramref name="env"/>. <see cref="PushSource.Promote"/> plans only new service keys
    /// (<c>Add</c>/<c>CreateSecret</c>, empty base, global ignored); <see cref="PushSource.EditedJson"/> uses the current remote state as base (it was exported from it); a key already in <paramref name="env"/> at service scope is a conflict (a global-only key gets a service override).
    /// <see cref="PushSource.LocalFiles"/> skips ignored keys; without <see cref="PushSource.LocalFiles.Delete"/> (upsert) a local
    /// deletion is not planned but returned as <see cref="SyncWarningKind.DeleteSkipped"/>. Edited and promoted JSON ignore neither.
    /// Throws <see cref="ArgumentException"/> naming the key for a null secret value.
    /// </summary>
    public async Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct)
    {
        var (snapshot, merged, raw, readWarnings) = await ReadRemoteAsync(binding, env, ct);
        var promote = source is PushSource.Promote;
        var @base = source switch { PushSource.Promote => new ConfigState(new Dictionary<string, StateItem>()), PushSource.EditedJson => merged, _ => snapshot };
        var remote = promote ? Service(merged) : merged;
        var json = source switch { PushSource.EditedJson e => e.Json, PushSource.Promote p => p.Json, _ => null };
        var local = json is null ? new ParsedEditJson(await ReadLocalAsync(binding, ct), []) : JsonProjection.FromEditJson(json, fingerprinter, KnownSecrets(@base, remote));
        if (promote)
        {
            local = local with { State = Service(local.State) };
        }

        var diff = ThreeWayDiff.Compute(@base, local.State, remote, fingerprinter);
        if (source is PushSource.LocalFiles)
        {
            diff = WithoutIgnored(diff, binding);
        }

        var plan = ThreeWayDiff.Plan(diff, raw, binding, env);

        var fallback = binding.VaultByEnvironment.GetValueOrDefault(env.Name) is { } bound ? Vault(bound) : MostFrequentVault(raw, ConfigScope.Service) ?? MostFrequentVault(raw, ConfigScope.Global);
        var changes = new List<Change>();
        List<SyncWarning> skipped = [];
        var requiresVaultChoice = false;
        foreach (var change in plan.Changes)
        {
            if (change.Kind == ChangeKind.Delete && source is PushSource.LocalFiles { Delete: false })
            {
                skipped.Add(new(SyncWarningKind.DeleteSkipped, change.Before!.Key, $"'{change.Before.Key}' was deleted locally; it is not deleted remotely (use --delete)."));
                continue;
            }

            if (change.Kind != ChangeKind.CreateSecret)
            {
                changes.Add(change);
                continue;
            }

            var item = diff.Items.First(i => i.Local?.Value is StateValue.Secret && ThreeWayDiff.SecretName(i, binding.Service) == change.SecretName);
            var scope = item.Local!.Scope;
            var created = local.NewSecrets.FirstOrDefault(n => n.Key == item.Key && n.Scope == scope);
            var value = created?.Value ?? (local.State.Items[item.Key].Value is StateValue.Plain plain ? new SecretString(plain.Value) : null);
            if (value is null)
            {
                continue;
            }

            var name = created?.Name ?? change.SecretName;
            var vault = (item.Local.Value as StateValue.Secret)?.SecretUri is { } known ? Vault(known) : fallback;
            requiresVaultChoice |= vault is null;
            changes.Add(change with
            {
                SecretName = name,
                SecretValue = value,
                After = vault is null ? null : new(item.Key, scope, new ConfigValue.KeyVaultRef(new($"{vault.AbsoluteUri.TrimEnd('/')}/secrets/{name}")), Label: change.Before?.Label ?? env.WriteLabel),
            });
        }

        List<SyncWarning> warnings = [.. readWarnings, .. plan.Warnings, .. skipped];
        return plan with { Changes = changes, RequiresVaultChoice = requiresVaultChoice, Warnings = warnings.Count == 0 ? Array.Empty<SyncWarning>() : warnings };
    }

    /// <summary>
    /// Creates Key Vault secrets first, then writes all changes to App Config in one call (references use the new secret URIs).
    /// Throws <see cref="InvalidOperationException"/> before any write on conflicts, a missing vault choice, an unconfirmed
    /// protected environment (name, ignoring case) or unconfirmed global changes. When nothing failed, the snapshot is updated only for keys touched by the plan.
    /// </summary>
    public async Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct)
    {
        if (plan.Conflicts.Count > 0)
        {
            throw new InvalidOperationException("Plan has conflicts; resolve them before applying.");
        }

        if (plan.RequiresVaultChoice)
        {
            throw new InvalidOperationException("Choose a Key Vault first.");
        }

        if (plan.Environment.IsProtected && !string.Equals(options.ConfirmedEnvironment, plan.Environment.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Applying to protected environment '{plan.Environment.Name}' requires typing its name.");
        }

        if (plan.TouchesGlobal && !options.ConfirmGlobal)
        {
            throw new InvalidOperationException("Plan changes global keys; confirm global changes.");
        }

        var changes = new List<Change>();
        foreach (var change in plan.Changes)
        {
            if (change.Kind != ChangeKind.CreateSecret)
            {
                changes.Add(change);
                continue;
            }

            var uri = await secretStore.SetAsync(Vault(((ConfigValue.KeyVaultRef)change.After!.Value).SecretUri), change.SecretName!, change.SecretValue!.Reveal(), ct);
            changes.Add(new(change.Before is null ? ChangeKind.Add : ChangeKind.Update, change.Before, change.After with { Value = new ConfigValue.KeyVaultRef(uri) }));
        }

        var result = await store.ApplyAsync(plan.Environment, plan.Binding.Service, changes, ct);
        if (result.Failed.Count == 0)
        {
            var (@base, remote, _, _) = await ReadRemoteAsync(plan.Binding, plan.Environment, ct);
            var items = new Dictionary<string, StateItem>(@base.Items, StringComparer.Ordinal);
            foreach (var key in plan.Changes.Select(c => (c.After ?? c.Before)!.Key))
            {
                if (remote.Items.TryGetValue(key, out var item))
                {
                    items[key] = item;
                }
                else
                {
                    items.Remove(key);
                }
            }

            await state.SaveSnapshotAsync(plan.Binding.RepoPath, plan.Environment.Name, new(items), ct);
        }

        return result;
    }

    private async Task<ConfigState> ReadLocalAsync(ProjectBinding binding, CancellationToken ct)
    {
        var (values, secrets) = await ReadTargetsAsync(binding, ct);
        return ToState([.. values, .. secrets]);
    }

    /// <summary>Reads the values target and the secrets target separately; a shared target is read once (as values).</summary>
    private async Task<(IReadOnlyList<LocalValue> Values, IReadOnlyList<LocalValue> Secrets)> ReadTargetsAsync(ProjectBinding binding, CancellationToken ct) => (
        await targets.First(t => t.Kind == binding.ValuesTarget.Kind).ReadAsync(binding.ValuesTarget, ct),
        binding.ValuesTarget == binding.SecretsTarget ? [] : await targets.First(t => t.Kind == binding.SecretsTarget.Kind).ReadAsync(binding.SecretsTarget, ct));

    private static ConfigState ToState(IEnumerable<LocalValue> local) =>
        new(local.ToDictionary(v => v.Key, v => new StateItem(v.Key, v.Scope, new StateValue.Plain(v.Value)), StringComparer.Ordinal));

    /// <summary>Drops items matched by <see cref="ProjectBinding.Ignore"/>; the scope matched is the provenance (remote, else base, else local).</summary>
    private static ConfigDiff WithoutIgnored(ConfigDiff diff, ProjectBinding binding) =>
        new([.. diff.Items.Where(i => !IgnoreRules.IsIgnored(binding.Ignore, i.Key, (i.Remote ?? i.Base ?? i.Local)!.Scope))]);

    /// <summary>Reads the environment's entries with one copy per <c>(Scope, Key)</c> across labels (ADR 0008).</summary>
    private async Task<(IReadOnlyList<ConfigEntry> Entries, IReadOnlyList<SyncWarning> Warnings)> ReadEntriesAsync(ConfigEnvironment env, string service, CancellationToken ct) =>
        LabelSelector.Select(await store.ReadAsync(env, service, ct).ToListAsync(ct), env);

    private async Task<(ConfigState Base, ConfigState Remote, IReadOnlyList<ConfigEntry> Raw, IReadOnlyList<SyncWarning> Warnings)> ReadRemoteAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
    {
        var (raw, warnings) = await ReadEntriesAsync(env, binding.Service, ct);
        var items = new Dictionary<string, StateItem>(StringComparer.Ordinal);
        foreach (var entry in ScopeMerger.Merge(raw))
        {
            StateValue? value = entry.Value switch
            {
                ConfigValue.Plain plain => new StateValue.Plain(plain.Value),
                ConfigValue.KeyVaultRef reference => new StateValue.Secret(reference.SecretUri, fingerprinter.Fingerprint(new SecretString(await secretStore.GetAsync(reference.SecretUri, ct)))),
                _ => null,
            };
            if (value is not null)
            {
                items[entry.Key] = new(entry.Key, entry.Scope, value);
            }
        }

        return (await state.LoadSnapshotAsync(binding.RepoPath, env.Name, ct) ?? new(new Dictionary<string, StateItem>()), new(items), raw, warnings);
    }

    private static Dictionary<string, StateValue.Secret> KnownSecrets(ConfigState @base, ConfigState remote) => @base.Items.Values.Concat(remote.Items.Values)
        .Select(i => i.Value).OfType<StateValue.Secret>().Where(s => s.SecretUri is not null)
        .GroupBy(s => ThreeWayDiff.SecretName(s.SecretUri!)).ToDictionary(g => g.Key, g => g.Last());

    private static ConfigState Service(ConfigState state) => new(state.Items.Where(i => i.Value.Scope == ConfigScope.Service).ToDictionary(i => i.Key, i => i.Value, StringComparer.Ordinal));

    private static Uri Vault(Uri uri) => new(uri.GetLeftPart(UriPartial.Authority));

    private static Uri? MostFrequentVault(IEnumerable<ConfigEntry> raw, ConfigScope scope) => raw.Where(e => e.Scope == scope)
        .Select(e => e.Value switch { ConfigValue.KeyVaultRef r => r.SecretUri, ConfigValue.ResolvedSecret r => r.SecretUri, _ => null })
        .OfType<Uri>().Select(Vault).GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.AbsoluteUri, StringComparer.Ordinal)
        .Select(g => g.Key).FirstOrDefault();
}
