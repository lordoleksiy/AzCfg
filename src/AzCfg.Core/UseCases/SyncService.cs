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

        if (!force && binding.ActiveEnvironment is { } active && await state.LoadSnapshotAsync(binding.RepoPath, active, ct) is { } snapshot)
        {
            var localState = await ReadLocalAsync(binding, ct);
            var changed = ThreeWayDiff.Compute(snapshot, localState, snapshot, fingerprinter).Items
                .Where(i => i.Kind is DiffKind.LocalAdded or DiffKind.LocalModified or DiffKind.LocalDeleted)
                .Select(i => i.Key).ToList();
            if (changed.Count > 0)
            {
                return new(env.Name, 0, 0, true, changed);
            }
        }

        var entries = ScopeMerger.Merge(await store.ReadAsync(env, binding.Service, ct).ToListAsync(ct));
        List<LocalValue> values = [], secrets = [];
        var items = new Dictionary<string, StateItem>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            switch (entry.Value)
            {
                case ConfigValue.Plain plain:
                    values.Add(new(entry.Key, plain.Value));
                    items[entry.Key] = new(entry.Key, entry.Scope, new StateValue.Plain(plain.Value));
                    break;
                case ConfigValue.KeyVaultRef reference:
                    var secret = new SecretString(await secretStore.GetAsync(reference.SecretUri, ct));
                    secrets.Add(new(entry.Key, secret.Reveal()));
                    items[entry.Key] = new(entry.Key, entry.Scope, new StateValue.Secret(reference.SecretUri, fingerprinter.Fingerprint(secret)));
                    break;
            }
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
        return new(env.Name, values.Count, secrets.Count, false, []);
    }

    public Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotImplementedException();

    public async Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct)
    {
        var existing = (await store.ReadAsync(to, binding.Service, ct).ToListAsync(ct))
            .Where(e => e.Scope == ConfigScope.Service).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var added = (await store.ReadAsync(from, binding.Service, ct).ToListAsync(ct))
            .Where(e => e.Scope == ConfigScope.Service && !existing.Contains(e.Key));
        return JsonProjection.ToEditJson(added, secretPlaceholders: true).ToJsonString(new() { WriteIndented = true });
    }

    public async Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
    {
        var (@base, remote, _) = await ReadRemoteAsync(binding, env, ct);
        return ThreeWayDiff.Compute(@base, await ReadLocalAsync(binding, ct), remote, fingerprinter);
    }

    public async Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct)
    {
        if (source is PushSource.Promote)
        {
            throw new NotSupportedException("Promote push is not supported yet.");
        }

        var (@base, remote, raw) = await ReadRemoteAsync(binding, env, ct);
        var local = source is PushSource.EditedJson edited
            ? JsonProjection.FromEditJson(edited.Json, fingerprinter, KnownSecrets(@base, remote))
            : new ParsedEditJson(await ReadLocalAsync(binding, ct), []);
        var diff = ThreeWayDiff.Compute(@base, local.State, remote, fingerprinter);
        var plan = ThreeWayDiff.Plan(diff, raw, binding, env);

        var fallback = binding.VaultByEnvironment.GetValueOrDefault(env.Name) is { } bound ? Vault(bound) : MostFrequentVault(raw, ConfigScope.Service) ?? MostFrequentVault(raw, ConfigScope.Global);
        var changes = new List<Change>();
        var requiresVaultChoice = false;
        foreach (var change in plan.Changes)
        {
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
                After = vault is null ? null : new(item.Key, scope, new ConfigValue.KeyVaultRef(new($"{vault.AbsoluteUri.TrimEnd('/')}/secrets/{name}"))),
            });
        }

        return plan with { Changes = changes, RequiresVaultChoice = requiresVaultChoice };
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
            var (@base, remote, _) = await ReadRemoteAsync(plan.Binding, plan.Environment, ct);
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
        var local = await targets.First(t => t.Kind == binding.ValuesTarget.Kind).ReadAsync(binding.ValuesTarget, ct);
        if (binding.ValuesTarget != binding.SecretsTarget)
        {
            local = [.. local, .. await targets.First(t => t.Kind == binding.SecretsTarget.Kind).ReadAsync(binding.SecretsTarget, ct)];
        }

        return new(local.ToDictionary(v => v.Key, v => new StateItem(v.Key, ConfigScope.Service, new StateValue.Plain(v.Value)), StringComparer.Ordinal));
    }

    private async Task<(ConfigState Base, ConfigState Remote, List<ConfigEntry> Raw)> ReadRemoteAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
    {
        var raw = await store.ReadAsync(env, binding.Service, ct).ToListAsync(ct);
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

        return (await state.LoadSnapshotAsync(binding.RepoPath, env.Name, ct) ?? new(new Dictionary<string, StateItem>()), new(items), raw);
    }

    private static Dictionary<string, StateValue.Secret> KnownSecrets(ConfigState @base, ConfigState remote) => @base.Items.Values.Concat(remote.Items.Values)
        .Select(i => i.Value).OfType<StateValue.Secret>().Where(s => s.SecretUri is not null)
        .GroupBy(s => ThreeWayDiff.SecretName(s.SecretUri!)).ToDictionary(g => g.Key, g => g.Last());

    private static Uri Vault(Uri uri) => new(uri.GetLeftPart(UriPartial.Authority));

    private static Uri? MostFrequentVault(IEnumerable<ConfigEntry> raw, ConfigScope scope) => raw.Where(e => e.Scope == scope)
        .Select(e => e.Value switch { ConfigValue.KeyVaultRef r => r.SecretUri, ConfigValue.ResolvedSecret r => r.SecretUri, _ => null })
        .OfType<Uri>().Select(Vault).GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.AbsoluteUri, StringComparer.Ordinal)
        .Select(g => g.Key).FirstOrDefault();
}
