using System.Text.Json;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using Microsoft.Extensions.Configuration;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// Base for local targets backed by a nested JSON file (<see cref="LocalFileFormat.NestedJson"/>, ADR 0006).
/// Reading uses <c>Microsoft.Extensions.Configuration.Json</c>, which already flattens nested objects into
/// colon-separated keys; writing uses <see cref="JsonProjection.ToNestedJson"/> and fully overwrites the file.
/// A missing file reads as an empty list. <see cref="CanHandle"/> and <see cref="InspectAsync"/> are left to
/// derived classes, since they differ per target kind (file location, project applicability, secret safety).
/// </summary>
public abstract class JsonFileTarget : ILocalTarget
{
    public abstract LocalTargetKind Kind { get; }

    public abstract bool CanHandle(ProjectInfo project);

    public abstract Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct);

    public Task<IReadOnlyList<LocalValue>> ReadAsync(LocalTargetBinding target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ct.ThrowIfCancellationRequested();

        var path = GetReadFilePath(target);
        if (path is null || !File.Exists(path))
        {
            return Task.FromResult<IReadOnlyList<LocalValue>>([]);
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(path, optional: true)
            .Build();

        IReadOnlyList<LocalValue> values = configuration.AsEnumerable()
            .Where(pair => pair.Value is not null)
            .Select(pair => new LocalValue(pair.Key, pair.Value!))
            .ToList();

        return Task.FromResult(values);
    }

    public async Task WriteAsync(LocalTargetBinding target, IReadOnlyList<LocalValue> values, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(values);
        ct.ThrowIfCancellationRequested();

        var json = JsonProjection.ToNestedJson(values);
        var path = GetFilePath(target);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, json.ToJsonString(), ct);
    }

    /// <summary>File to read, or <c>null</c> when there is none yet. Defaults to <see cref="GetFilePath"/>.</summary>
    protected virtual string? GetReadFilePath(LocalTargetBinding target) => GetFilePath(target);

    /// <summary>Resolves the JSON file path for a binding. Derived classes know where their file lives.</summary>
    protected abstract string GetFilePath(LocalTargetBinding target);
}
