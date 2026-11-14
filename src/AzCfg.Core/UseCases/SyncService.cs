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
            var local = await targets.First(t => t.Kind == binding.ValuesTarget.Kind).ReadAsync(binding.ValuesTarget, ct);
            if (binding.ValuesTarget != binding.SecretsTarget)
            {
                local = [.. local, .. await secretsTarget.ReadAsync(binding.SecretsTarget, ct)];
            }

            var localState = new ConfigState(local.ToDictionary(v => v.Key, v => new StateItem(v.Key, ConfigScope.Service, new StateValue.Plain(v.Value)), StringComparer.Ordinal));
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

    public Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct) => throw new NotImplementedException();

    public Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotImplementedException();

    public Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct) => throw new NotImplementedException();
}
