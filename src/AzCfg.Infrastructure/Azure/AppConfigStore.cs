using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Azure.Core;
using Azure.Data.AppConfiguration;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Infrastructure.Azure;

/// <summary>
/// Azure App Configuration store. Reads <c>global:*</c> and <c>&lt;service&gt;:*</c> keys as entries; keys of
/// other services are skipped. Writing is not implemented yet.
/// </summary>
public sealed class AppConfigStore(TokenCredential credential) : IConfigStore
{
    private readonly ConcurrentDictionary<Uri, ConfigurationClient> _clients = new();

    public async IAsyncEnumerable<ConfigEntry> ReadAsync(
        ConfigEnvironment env, string service, [EnumeratorCancellation] CancellationToken ct)
    {
        var selector = new SettingSelector { LabelFilter = env.Label ?? SettingSelector.Any };
        selector.KeyFilter = "global:*";
        await foreach (var setting in Client(env).GetConfigurationSettingsAsync(selector, ct))
        {
            if (ToEntry(setting, service) is { } entry)
            {
                yield return entry;
            }
        }

        selector.KeyFilter = service + ":*";
        await foreach (var setting in Client(env).GetConfigurationSettingsAsync(selector, ct))
        {
            if (ToEntry(setting, service) is { } entry)
            {
                yield return entry;
            }
        }
    }

    public async IAsyncEnumerable<string> ListServicePrefixesAsync(
        ConfigEnvironment env, [EnumeratorCancellation] CancellationToken ct)
    {
        var selector = new SettingSelector { KeyFilter = "*:*", Fields = SettingFields.Key };
        var seen = new HashSet<string>(StringComparer.Ordinal) { "global" };
        await foreach (var setting in Client(env).GetConfigurationSettingsAsync(selector, ct))
        {
            var prefix = setting.Key[..setting.Key.IndexOf(':')];
            if (prefix.Length > 0 && seen.Add(prefix))
            {
                yield return prefix;
            }
        }
    }

    public Task<ApplyResult> ApplyAsync(
        ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct) =>
        throw new NotImplementedException();

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
