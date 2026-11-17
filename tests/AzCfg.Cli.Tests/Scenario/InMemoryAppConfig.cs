using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Cli.Tests.Scenario;

/// <summary>
/// In-memory Azure App Configuration with the REST semantics <c>AppConfigStore</c> relies on (ADR 0008): settings keyed by
/// <c>(store, key, label)</c>; every write gets a new ETag; reads select every label when <see cref="ConfigEnvironment.Label"/> is
/// <c>null</c>, else unlabeled settings first and then the environment's label, each query ordered by key then label;
/// <see cref="ConfigEntry.Label"/> is the setting's label. Add fails when <c>(key, label)</c> exists; Update/Delete fail on a missing
/// setting or a stale ETag (412 → <see cref="ApplyResult.Failed"/>). Writes target exactly the entry's label.
/// </summary>
public sealed class InMemoryAppConfig : IConfigStore
{
    private readonly Dictionary<(Uri Store, string Key, string? Label), RemoteSetting> _settings = [];
    private int _etags;

    /// <summary>Successful writes (add, update, delete) made through <see cref="ApplyAsync"/>.</summary>
    public int Writes { get; private set; }

    public RemoteSetting Set(string store, string key, string value, string? label = null) =>
        Put(new Uri(store), key, label, new ConfigValue.Plain(value));

    public RemoteSetting SetSecretReference(string store, string key, Uri secretUri, string? label = null) =>
        Put(new Uri(store), key, label, new ConfigValue.KeyVaultRef(secretUri));

    public RemoteSetting? Get(string store, string key, string? label = null) => _settings.GetValueOrDefault((new Uri(store), key, label));

    /// <summary>Every setting of <paramref name="store"/> ordered by key then label (unlabeled first).</summary>
    public IReadOnlyList<RemoteSetting> Settings(string store) => [.. Query(new Uri(store), "", _ => true)];

    /// <summary>All copies of <paramref name="key"/> across labels, unlabeled first.</summary>
    public IReadOnlyList<RemoteSetting> Copies(string store, string key) => [.. Settings(store).Where(s => s.Key == key)];

    public IAsyncEnumerable<ConfigEntry> ReadAsync(ConfigEnvironment env, string service, CancellationToken ct) =>
        ((string[])["global:", service + ":"])
            .SelectMany(prefix => ReadLabels(env).SelectMany(label => Query(env.StoreEndpoint, prefix, label)))
            .Select(s => KeyMapper.TryParseStoreKey(s.Key, service, out var scope, out var path) ? new ConfigEntry(path, scope, s.Value, s.ETag, s.Label) : null)
            .OfType<ConfigEntry>()
            .ToList()
            .ToAsyncEnumerable();

    public IAsyncEnumerable<string> ListServicePrefixesAsync(ConfigEnvironment env, CancellationToken ct) =>
        ReadLabels(env)
            .SelectMany(label => Query(env.StoreEndpoint, "", label))
            .Select(s => s.Key[..Math.Max(s.Key.IndexOf(':', StringComparison.Ordinal), 0)])
            .Where(p => p.Length > 0 && p != "global")
            .Distinct()
            .ToList()
            .ToAsyncEnumerable();

    public Task<IReadOnlyList<string>> ListLabelsAsync(Uri storeEndpoint, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>([.. Query(storeEndpoint, "", _ => true).Select(s => s.Label).OfType<string>().Distinct().Order(StringComparer.Ordinal)]);

    public Task<ApplyResult> ApplyAsync(ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct)
    {
        var applied = 0;
        List<string> failed = [];
        foreach (var change in changes.Where(c => c.Kind != ChangeKind.CreateSecret))
        {
            var entry = (change.Kind == ChangeKind.Delete ? change.Before : change.After)!;
            var key = KeyMapper.ToStoreKey(entry.Scope, service, entry.Key);
            var id = (env.StoreEndpoint, key, entry.Label);
            var exists = _settings.TryGetValue(id, out var current);
            var ok = change.Kind switch
            {
                ChangeKind.Add => !exists,
                _ => exists && current!.ETag == change.Before?.ETag,
            };

            if (!ok)
            {
                failed.Add(key);
                continue;
            }

            if (change.Kind == ChangeKind.Delete)
            {
                _settings.Remove(id);
            }
            else
            {
                Put(env.StoreEndpoint, key, entry.Label, entry.Value switch
                {
                    ConfigValue.ResolvedSecret secret => new ConfigValue.KeyVaultRef(secret.SecretUri),
                    var value => value,
                });
            }

            applied++;
            Writes++;
        }

        return Task.FromResult(new ApplyResult(applied, failed));
    }

    private static IReadOnlyList<Func<string?, bool>> ReadLabels(ConfigEnvironment env) =>
        env.Label is null ? [_ => true] : [l => l is null, l => l == env.Label];

    private IEnumerable<RemoteSetting> Query(Uri store, string keyPrefix, Func<string?, bool> label) => _settings
        .Where(s => s.Key.Store == store && s.Key.Key.StartsWith(keyPrefix, StringComparison.Ordinal) && label(s.Key.Label))
        .Select(s => s.Value)
        .OrderBy(s => s.Key, StringComparer.Ordinal)
        .ThenBy(s => s.Label is not null)
        .ThenBy(s => s.Label, StringComparer.Ordinal);

    private RemoteSetting Put(Uri store, string key, string? label, ConfigValue value) =>
        _settings[(store, key, label)] = new RemoteSetting(key, label, value, $"etag-{++_etags}");
}
