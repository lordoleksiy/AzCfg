using System.Text.Json;

namespace AzCfg.Ui.Application.Workspace;

/// <summary>
/// <see cref="IWorkspaceRegistry"/> backed by <c>&lt;root&gt;/ui/workspaces.json</c> (atomic write). An unreadable file counts as
/// an empty list. Never touches the CLI's <c>projects.json</c>.
/// </summary>
public sealed class WorkspaceRegistry(string root, TimeProvider time) : IWorkspaceRegistry
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _file = Path.Combine(root, "ui", "workspaces.json");

    public async Task<IReadOnlyList<WorkspaceEntry>> ListAsync(CancellationToken ct) =>
        await Locked(async () => Order(await LoadAsync(ct)), ct);

    public Task AddOrTouchAsync(string rootPath, CancellationToken ct) =>
        Update(entries =>
        {
            var existing = entries.Find(e => PathKeys.Same(e.RootPath, rootPath));
            entries.RemoveAll(e => PathKeys.Same(e.RootPath, rootPath));
            entries.Add(new WorkspaceEntry(existing?.RootPath ?? PathKeys.Full(rootPath), existing?.DisplayName ?? NameOf(rootPath), time.GetUtcNow(), existing?.Pinned ?? false));
        }, ct);

    public Task RemoveAsync(string rootPath, CancellationToken ct) =>
        Update(entries => entries.RemoveAll(e => PathKeys.Same(e.RootPath, rootPath)), ct);

    public Task SetPinnedAsync(string rootPath, bool pinned, CancellationToken ct) =>
        Update(entries =>
        {
            var index = entries.FindIndex(e => PathKeys.Same(e.RootPath, rootPath));
            if (index >= 0)
            {
                entries[index] = entries[index] with { Pinned = pinned };
            }
        }, ct);

    private static IReadOnlyList<WorkspaceEntry> Order(List<WorkspaceEntry> entries) =>
        [.. entries.OrderByDescending(e => e.Pinned).ThenByDescending(e => e.LastOpened)];

    private static string NameOf(string rootPath)
    {
        var full = PathKeys.Full(rootPath);
        var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? full : name;
    }

    private async Task<T> Locked<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task Update(Action<List<WorkspaceEntry>> change, CancellationToken ct) =>
        Locked(async () =>
        {
            var entries = await LoadAsync(ct);
            change(entries);
            await SaveAsync(entries, ct);
            return true;
        }, ct);

    private async Task<List<WorkspaceEntry>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_file))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_file);
            return await JsonSerializer.DeserializeAsync<List<WorkspaceEntry>>(stream, Options, ct) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task SaveAsync(List<WorkspaceEntry> entries, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temp = $"{_file}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(entries, Options), ct);
        File.Move(temp, _file, overwrite: true);
    }
}
