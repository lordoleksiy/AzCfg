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
/// other services are skipped. Applies Add/Update/Delete with optimistic concurrency; HTTP 412 puts the key in
/// <see cref="ApplyResult.Failed"/>, other errors propagate. <see cref="ChangeKind.CreateSecret"/> is skipped (Key Vault).
/// </summary>
public sealed class AppConfigStore(TokenCredential credential) : IConfigStore
{
    private const string NoLabel = "\0";

    private readonly ConcurrentDictionary<Uri, ConfigurationClient> _clients = new();

    public async IAsyncEnumerable<ConfigEntry> ReadAsync(
        ConfigEnvironment env, string service, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var keyFilter in (string[])["global:*", $"{service}:*"])
        {
            var selector = new SettingSelector { KeyFilter = keyFilter, LabelFilter = env.Label ?? NoLabel };
            await foreach (var setting in Client(env).GetConfigurationSettingsAsync(selector, ct))
            {
                if (ToEntry(setting, service) is { } entry)
                {
                    yield return entry;
                }
            }
        }
    }

    public async IAsyncEnumerable<string> ListServicePrefixesAsync(
        ConfigEnvironment env, [EnumeratorCancellation] CancellationToken ct)
    {
        var selector = new SettingSelector { LabelFilter = env.Label ?? NoLabel, Fields = SettingFields.Key };
        List<string> keys = [];
        await foreach (var setting in Client(env).GetConfigurationSettingsAsync(selector, ct))
        {
            keys.Add(setting.Key);
        }

        foreach (var prefix in ToServicePrefixes(keys))
        {
            yield return prefix;
        }
    }

    public async Task<ApplyResult> ApplyAsync(
        ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct)
    {
        var client = Client(env);
        var applied = 0;
        List<string> failed = [];
        foreach (var change in changes.Where(c => c.Kind != ChangeKind.CreateSecret))
        {
            var setting = ToSetting(change, service, env.Label);
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
    /// Builds the setting for an Add/Update (from <c>After</c>) or Delete (from <c>Before</c>) change; the ETag comes
    /// from <c>Before</c>. Key Vault values become secret references; secret text is never written.
    /// </summary>
    internal static ConfigurationSetting ToSetting(Change change, string service, string? label)
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
            ConfigValue.KeyVaultRef reference => ConfigurationModelFactory.SecretReferenceConfigurationSetting(key, reference.SecretUri, label, eTag),
            ConfigValue.ResolvedSecret secret => ConfigurationModelFactory.SecretReferenceConfigurationSetting(key, secret.SecretUri, label, eTag),
            ConfigValue.Plain plain => ConfigurationModelFactory.ConfigurationSetting(key, plain.Value, label, eTag: eTag),
            _ => throw new ArgumentException("Unsupported config value.", nameof(change)),
        };
    }

    internal static IEnumerable<string> ToServicePrefixes(IEnumerable<string> keys) =>
        keys.Select(k => k[..Math.Max(k.IndexOf(':'), 0)]).Where(p => p.Length > 0 && p != "global").Distinct();

    internal static ConfigEntry? ToEntry(ConfigurationSetting setting, string service) =>
        KeyMapper.TryParseStoreKey(setting.Key, service, out var scope, out var path)
            ? new ConfigEntry(
                path,
                scope,
                setting is SecretReferenceConfigurationSetting reference
                    ? new ConfigValue.KeyVaultRef(reference.SecretId)
                    : new ConfigValue.Plain(setting.Value ?? ""),
                setting.ETag == default ? null : setting.ETag.ToString())
            : null;

    private ConfigurationClient Client(ConfigEnvironment env) =>
        _clients.GetOrAdd(env.StoreEndpoint, endpoint => new ConfigurationClient(endpoint, credential));
}
