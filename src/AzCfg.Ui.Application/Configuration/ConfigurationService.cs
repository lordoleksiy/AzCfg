using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Security;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>
/// Browse backend (FR-B1…B7). The active environment is loaded from <c>DiffAsync</c> (statuses, secrets resolved only to
/// fingerprints), the remote configuration and a separate read of each local target; any other environment is remote-only
/// and never touches Key Vault. Local values from a secrets target and remote Key Vault references are masked (AD-6).
/// </summary>
internal sealed class ConfigurationService(ISyncService sync, LocalTargetAccess local, TimeProvider time) : IConfigurationService
{
    public async Task<ConfigurationView> LoadAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
    {
        var active = string.Equals(binding.ActiveEnvironment, env.Name, StringComparison.OrdinalIgnoreCase);
        var remote = await sync.ReadRemoteConfigAsync(binding, env, ct);
        var diff = active ? (await sync.DiffAsync(binding, env, ct)).Items.ToDictionary(i => i.Key, StringComparer.Ordinal) : [];
        var locals = active ? LocalTargetAccess.ByKey(await local.ReadAllAsync(binding, ct)) : [];
        var entries = remote.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var shadowed = remote.Shadowed.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var rows = diff.Keys.Concat(entries.Keys).Concat(locals.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(key => Row(binding, key, diff.GetValueOrDefault(key), entries.GetValueOrDefault(key), shadowed.GetValueOrDefault(key), locals.GetValueOrDefault(key)))
            .ToList();
        return new(binding, env, !active, rows, remote.Warnings, time.GetUtcNow());
    }

    public string ToJson(ConfigurationView view, ConfigSide side) => side switch
    {
        ConfigSide.Remote => ConfigJson.Remote(view.Rows.Where(r => r.Remote is not null)
            .Select(r => RemoteEntry(r.Key, r.Remote!)).OfType<ConfigEntry>()),
        _ => ConfigJson.Local(view.Rows.Where(r => r.Local is not null)
            .Select(r => (new LocalValue(r.Key, r.Local!.Value ?? "", r.Local.Scope), r.Local.Masked))),
    };

    private static ConfigEntry? RemoteEntry(string key, RemoteCell cell) => cell switch
    {
        { IsSecret: true, SecretUri: { } uri } => new(key, cell.Scope, new ConfigValue.KeyVaultRef(uri), null, cell.Label),
        { IsSecret: true } => null,
        _ => new(key, cell.Scope, new ConfigValue.Plain(cell.Value ?? ""), null, cell.Label),
    };

    private static ConfigRow Row(ProjectBinding binding, string key, DiffItem? diff, ConfigEntry? remote, ConfigEntry? shadowed, SourcedLocalValue? local)
    {
        var scope = remote?.Scope ?? diff?.Base?.Scope ?? local?.Value.Scope ?? ConfigScope.Service;
        var ignored = IgnoreRules.IsIgnored(binding.Ignore, key, scope);
        var secret = SecretPolicy.IsSecret(diff, remote, local?.Source, binding);
        return new(key, ignored ? null : diff?.Kind, secret, ignored, LocalCell(local, secret), remote is null ? null : RemoteCell(remote, shadowed));
    }

    private static LocalCell? LocalCell(SourcedLocalValue? local, bool secret) =>
        local is null ? null : new(secret ? null : local.Value.Value, secret, local.Source, local.Value.Scope);

    private static RemoteCell RemoteCell(ConfigEntry remote, ConfigEntry? shadowed) => remote.Value switch
    {
        ConfigValue.KeyVaultRef r => SecretCell(remote, r.SecretUri),
        ConfigValue.ResolvedSecret r => SecretCell(remote, r.SecretUri),
        ConfigValue.Plain p => new(p.Value, false, null, null, null, remote.Scope, remote.Label, (shadowed?.Value as ConfigValue.Plain)?.Value),
        _ => throw new InvalidOperationException("Unknown config value."),
    };

    private static RemoteCell SecretCell(ConfigEntry remote, Uri uri) =>
        new(null, true, uri, KeyMapper.SecretNameOf(uri), uri.Host, remote.Scope, remote.Label, null);
}
