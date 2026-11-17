using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Infrastructure.Azure;

/// <summary>
/// Azure App Configuration store. Reads <c>global:*</c> and <c>&lt;service&gt;:*</c> keys as entries; keys of
/// other services are skipped. Labels follow ADR 0008: an environment without a label (store-per-environment mode)
/// reads every label; a labelled environment reads unlabeled keys plus keys with its label. Every entry carries the
/// label of its setting, and writes target exactly the entry's label (<c>After</c> for Add/Update, <c>Before</c> for
/// Delete), never the environment's. Applies Add/Update/Delete with optimistic concurrency; HTTP 412 puts the key in
/// <see cref="ApplyResult.Failed"/>, other errors propagate. <see cref="ChangeKind.CreateSecret"/> is skipped (Key Vault).
/// </summary>
public sealed class AppConfigStore(TokenCredential credential) : IConfigStore
{
    private const string NoLabel = "\0";

    private readonly ConcurrentDictionary<Uri, ConfigurationClient> _clients = new();

    public async IAsyncEnumerable<ConfigEntry> ReadAsync(
        ConfigEnvironment env, string service, [EnumeratorCancellation] CancellationToken ct)
    {
        var client = Client(env.StoreEndpoint);
        foreach (var keyFilter in (string[])["global:*", $"{service}:*"])
        {
            foreach (var label in ReadLabels(env))
            {
                await foreach (var setting in client.GetConfigurationSettingsAsync(CreateSelector(keyFilter, label), ct))
                {
                    if (ToEntry(setting, service) is { } entry)
                    {
                        yield return entry;
                    }
                }
            }
        }
    }

    public async IAsyncEnumerable<string> ListServicePrefixesAsync(
        ConfigEnvironment env, [EnumeratorCancellation] CancellationToken ct)
    {
        var client = Client(env.StoreEndpoint);
        List<string> keys = [];
        foreach (var label in ReadLabels(env))
        {
            var selector = CreateSelector(keyFilter: null, label);
            selector.Fields = SettingFields.Key;
            await foreach (var setting in client.GetConfigurationSettingsAsync(selector, ct))
            {
                keys.Add(setting.Key);
            }
        }

        foreach (var prefix in ToServicePrefixes(keys))
        {
            yield return prefix;
        }
    }

    public async Task<IReadOnlyList<string>> ListLabelsAsync(Uri storeEndpoint, CancellationToken ct)
    {
        var labels = new SortedSet<string>(StringComparer.Ordinal);
        var selector = new SettingSelector { Fields = SettingFields.Label };
        await foreach (var setting in Client(storeEndpoint).GetConfigurationSettingsAsync(selector, ct))
        {
            if (!string.IsNullOrEmpty(setting.Label))
            {
                labels.Add(setting.Label);
            }
        }

        return [.. labels];
    }

    public async Task<ApplyResult> ApplyAsync(
        ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct)
    {
        var client = Client(env.StoreEndpoint);
        var applied = 0;
        List<string> failed = [];
        foreach (var change in changes.Where(c => c.Kind != ChangeKind.CreateSecret))
        {
            var setting = ToSetting(change, service);
            try
            {
                _ = change.Kind switch
                {
                    ChangeKind.Add => (await client.AddConfigurationSettingAsync(setting, ct)).GetRawResponse(),
                    ChangeKind.Update => (await client.SetConfigurationSettingAsync(setting, onlyIfUnchanged: true, ct)).GetRawResponse(),
                    _ => await client.DeleteConfigurationSettingAsync(setting, onlyIfUnchanged: true, ct),
                };
                applied++;
            }
            catch (RequestFailedException e) when (e.Status == 412)
            {
                failed.Add(setting.Key);
            }
        }

        return new ApplyResult(applied, failed);
    }

    /// <summary>
    /// Builds a query selector for <paramref name="keyFilter"/> (<c>null</c> = any key) and
    /// <paramref name="label"/>; a <c>null</c> label leaves the label filter unset, which matches any label.
    /// </summary>
    internal static SettingSelector CreateSelector(string? keyFilter, string? label) =>
        new() { KeyFilter = keyFilter, LabelFilter = label };

    /// <summary>
    /// Label filters to query for <paramref name="env"/>: <c>[null]</c> (one query, every label) when the environment
    /// has no label; otherwise unlabeled keys first, then keys with the environment's label.
    /// </summary>
    internal static IReadOnlyList<string?> ReadLabels(ConfigEnvironment env) =>
        env.Label is null ? [null] : [NoLabel, env.Label];

    /// <summary>
    /// Builds the setting for an Add/Update (from <c>After</c>) or Delete (from <c>Before</c>) change; the key and
    /// label come from that entry, the ETag from <c>Before</c>. Key Vault values become secret references; secret
    /// text is never written.
    /// </summary>
    internal static ConfigurationSetting ToSetting(Change change, string service)
    {
        if (change.Kind == ChangeKind.CreateSecret)
        {
            throw new ArgumentException("CreateSecret targets Key Vault, not App Configuration.", nameof(change));
        }

        var entry = (change.Kind == ChangeKind.Delete ? change.Before : change.After)!;
        var key = KeyMapper.ToStoreKey(entry.Scope, service, entry.Key);
        var eTag = change.Before?.ETag is { } tag ? new ETag(tag) : default;
        return entry.Value switch
        {
            ConfigValue.KeyVaultRef reference => ConfigurationModelFactory.SecretReferenceConfigurationSetting(key, reference.SecretUri, entry.Label, eTag),
            ConfigValue.ResolvedSecret secret => ConfigurationModelFactory.SecretReferenceConfigurationSetting(key, secret.SecretUri, entry.Label, eTag),
            ConfigValue.Plain plain => ConfigurationModelFactory.ConfigurationSetting(key, plain.Value, entry.Label, eTag: eTag),
            _ => throw new ArgumentException("Unsupported config value.", nameof(change)),
        };
    }

    internal static IEnumerable<string> ToServicePrefixes(IEnumerable<string> keys) =>
        keys.Select(k => k[..Math.Max(k.IndexOf(':'), 0)]).Where(p => p.Length > 0 && p != "global").Distinct();

    /// <summary>
    /// Maps a setting of <paramref name="service"/> or <c>global</c> to an entry with the setting's ETag and label
    /// (empty label = <c>null</c>); keys of other services or malformed keys yield <c>null</c>.
    /// </summary>
    internal static ConfigEntry? ToEntry(ConfigurationSetting setting, string service) =>
        KeyMapper.TryParseStoreKey(setting.Key, service, out var scope, out var path)
            ? new ConfigEntry(
                path,
                scope,
                setting is SecretReferenceConfigurationSetting reference
                    ? new ConfigValue.KeyVaultRef(reference.SecretId)
                    : new ConfigValue.Plain(setting.Value ?? ""),
                setting.ETag == default ? null : setting.ETag.ToString(),
                string.IsNullOrEmpty(setting.Label) ? null : setting.Label)
            : null;

    private ConfigurationClient Client(Uri storeEndpoint) =>
        _clients.GetOrAdd(storeEndpoint, endpoint => new ConfigurationClient(endpoint, credential));
}
