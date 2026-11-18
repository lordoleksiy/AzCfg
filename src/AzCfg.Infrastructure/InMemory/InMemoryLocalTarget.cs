using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Infrastructure.InMemory;

/// <summary>
/// In-memory user-secrets file per <see cref="LocalTargetBinding"/>: handles every project and is safe for secrets. Writes replace the
/// file and are validated as nested JSON like the real target (no duplicate keys, no key that is both a value and a parent).
/// The <c>Local</c>/<c>Scope</c>/<c>SetLocal</c>/<c>RemoveLocal</c> helpers edit the single written file, as a developer would
/// (<c>SetLocal</c> keeps the key's scope, like editing a value next to its <c>//@global</c> marker).
/// <c>SetLocal</c>/<c>Scope</c>/<c>RemoveLocal</c> throw <see cref="InvalidOperationException"/> unless exactly one file was
/// written; <c>Scope</c>/<c>RemoveLocal</c> also unless the key is present exactly once.
/// </summary>
public sealed class InMemoryLocalTarget : ILocalTarget
{
    private readonly Dictionary<LocalTargetBinding, List<LocalValue>> _files = [];

    public LocalTargetKind Kind => LocalTargetKind.UserSecrets;

    public int Writes { get; private set; }

    public bool CanHandle(ProjectInfo project) => true;

    public Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct) =>
        Task.FromResult(new LocalTargetOption(target, _files.ContainsKey(target), IsSafeForSecrets: true));

    public Task EnsureGitIgnoredAsync(LocalTargetBinding target, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<LocalValue>> ReadAsync(LocalTargetBinding target, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LocalValue>>(_files.TryGetValue(target, out var values) ? [.. values] : []);

    public Task WriteAsync(LocalTargetBinding target, IReadOnlyList<LocalValue> values, CancellationToken ct)
    {
        _ = JsonProjection.ToNestedJson(values);
        _files[target] = [.. values];
        Writes++;
        return Task.CompletedTask;
    }

    /// <summary>The single file's values by key (empty before the first write).</summary>
    public Dictionary<string, string> Values() =>
        _files.Count == 0 ? [] : File().ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);

    public string? Local(string key) => Values().GetValueOrDefault(key);

    /// <summary>Scope of <paramref name="key"/> in the single file (<see cref="ConfigScope.Global"/> = marked <c>//@global</c>).</summary>
    public ConfigScope Scope(string key) => File().Single(v => v.Key == key).Scope;

    public void SetLocal(string key, string value)
    {
        var file = File();
        var index = file.FindIndex(v => v.Key == key);
        if (index >= 0)
            file[index] = file[index] with { Value = value };
        else
            file.Add(new(key, value));
    }

    public void RemoveLocal(string key)
    {
        if (File().RemoveAll(v => v.Key == key) != 1)
            throw new InvalidOperationException($"Key '{key}' is not in the local file.");
    }

    private List<LocalValue> File() => _files.Single().Value;
}
