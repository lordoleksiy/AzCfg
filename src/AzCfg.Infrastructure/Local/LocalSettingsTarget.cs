using System.Text.Json;
using System.Text.Json.Nodes;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// <see cref="LocalTargetKind.LocalSettings"/>: Azure Functions <c>local.settings.json</c> next to the project's
/// <c>.csproj</c>, offered only for <see cref="ProjectInfo.IsAzureFunctions"/> projects (ADR 0006).
/// <see cref="LocalFileFormat.FlatDictionary"/>: only the <c>Values</c> section is read and written, keys keep their
/// <c>A:B</c> path as-is; other sections (<c>IsEncrypted</c>, <c>Host</c>, <c>ConnectionStrings</c>) are kept as they were,
/// and a missing file is created as <c>{ "IsEncrypted": false, "Values": {} }</c>. No <c>//@global</c> markers are
/// written or read (Functions Core Tools acceptance of comments is not verified), so every value has
/// <see cref="ConfigScope.Service"/>. Secret safety is the same as <see cref="AppSettingsLocalTarget"/>.
/// </summary>
public sealed class LocalSettingsTarget : ILocalTarget
{
    private const string FileName = "local.settings.json";
    private const string ValuesSection = "Values";

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public LocalTargetKind Kind => LocalTargetKind.LocalSettings;

    public bool CanHandle(ProjectInfo project) => project.IsAzureFunctions;

    public async Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = GetFilePath(target);
        return new LocalTargetOption(target, File.Exists(path), await GitIgnoreGuard.IsSafeForSecretsAsync(path, ct));
    }

    public async Task<IReadOnlyList<LocalValue>> ReadAsync(LocalTargetBinding target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = GetFilePath(target);
        if (!File.Exists(path))
        {
            return [];
        }

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path, ct), documentOptions: ReadOptions) as JsonObject;
        return root?[ValuesSection] is JsonObject values
            ? [.. values.Where(p => p.Value is JsonValue).Select(p => new LocalValue(p.Key, p.Value!.ToString()))]
            : [];
    }

    public async Task WriteAsync(LocalTargetBinding target, IReadOnlyList<LocalValue> values, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(values);

        var path = GetFilePath(target);
        var root = File.Exists(path)
            ? JsonNode.Parse(await File.ReadAllTextAsync(path, ct), documentOptions: ReadOptions)!.AsObject()
            : new JsonObject { ["IsEncrypted"] = false };

        var section = new JsonObject();
        foreach (var value in values.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            section.Add(value.Key, value.Value);
        }

        root[ValuesSection] = section;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, root.ToJsonString(WriteOptions), ct);
    }

    public Task EnsureGitIgnoredAsync(LocalTargetBinding target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        return GitIgnoreGuard.EnsureIgnoredAsync(GetFilePath(target), ct);
    }

    private static string GetFilePath(LocalTargetBinding target) =>
        Path.Combine(Path.GetDirectoryName(target.ProjectPath) ?? "", FileName);
}
