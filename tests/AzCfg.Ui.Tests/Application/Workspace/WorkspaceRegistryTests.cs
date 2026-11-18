using AzCfg.Ui.Application.Workspace;
using Microsoft.Extensions.Time.Testing;

namespace AzCfg.Ui.Tests.Application.Workspace;

public sealed class WorkspaceRegistryTests : IDisposable
{
    private readonly string _state = Path.Combine(Path.GetTempPath(), $"azcfg-reg-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    private WorkspaceRegistry Registry() => new(_state, _time);

    public void Dispose()
    {
        if (Directory.Exists(_state))
        {
            Directory.Delete(_state, true);
        }
    }

    [Fact]
    public async Task ListAsync_NoFile_Empty() =>
        Assert.Empty(await Registry().ListAsync(default));

    [Fact]
    public async Task AddOrTouchAsync_NewRoot_PersistsUnderUiFolder()
    {
        await Registry().AddOrTouchAsync(Path.Combine(_state, "repos", "Crm"), default);

        Assert.True(File.Exists(Path.Combine(_state, "ui", "workspaces.json")));
        var entry = Assert.Single(await Registry().ListAsync(default));
        Assert.Equal("Crm", entry.DisplayName);
        Assert.False(entry.Pinned);
    }

    [Fact]
    public async Task AddOrTouchAsync_ExistingRoot_UpdatesLastOpenedAndKeepsPin()
    {
        var root = Path.Combine(_state, "repos", "Crm");
        var registry = Registry();
        await registry.AddOrTouchAsync(root, default);
        await registry.SetPinnedAsync(root, true, default);
        _time.Advance(TimeSpan.FromHours(1));

        await registry.AddOrTouchAsync(root, default);

        var entry = Assert.Single(await registry.ListAsync(default));
        Assert.True(entry.Pinned);
        Assert.Equal(_time.GetUtcNow(), entry.LastOpened);
    }

    [Fact]
    public async Task ListAsync_PinnedFirstThenLastOpenedDescending()
    {
        var registry = Registry();
        await registry.AddOrTouchAsync(Path.Combine(_state, "a"), default);
        _time.Advance(TimeSpan.FromMinutes(1));
        await registry.AddOrTouchAsync(Path.Combine(_state, "b"), default);
        _time.Advance(TimeSpan.FromMinutes(1));
        await registry.AddOrTouchAsync(Path.Combine(_state, "c"), default);
        await registry.SetPinnedAsync(Path.Combine(_state, "a"), true, default);

        Assert.Equal(["a", "c", "b"], (await registry.ListAsync(default)).Select(e => e.DisplayName));
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntryAndLeavesProjectsJsonUntouched()
    {
        Directory.CreateDirectory(_state);
        var projects = Path.Combine(_state, "projects.json");
        await File.WriteAllTextAsync(projects, "{\"keep\":true}");
        var registry = Registry();
        await registry.AddOrTouchAsync(Path.Combine(_state, "a"), default);

        await registry.RemoveAsync(Path.Combine(_state, "a"), default);

        Assert.Empty(await registry.ListAsync(default));
        Assert.Equal("{\"keep\":true}", await File.ReadAllTextAsync(projects));
    }

    [Fact]
    public async Task ListAsync_CorruptFile_Empty()
    {
        Directory.CreateDirectory(Path.Combine(_state, "ui"));
        await File.WriteAllTextAsync(Path.Combine(_state, "ui", "workspaces.json"), "{ not json");

        Assert.Empty(await Registry().ListAsync(default));
    }

    [Fact]
    public async Task AddOrTouchAsync_LeavesNoTempFiles()
    {
        await Registry().AddOrTouchAsync(Path.Combine(_state, "a"), default);

        Assert.Equal(["workspaces.json"], Directory.GetFiles(Path.Combine(_state, "ui")).Select(Path.GetFileName));
    }
}
