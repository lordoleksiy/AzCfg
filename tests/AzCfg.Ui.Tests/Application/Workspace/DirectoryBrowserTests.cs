using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Application.Workspace;

public sealed class DirectoryBrowserTests : IDisposable
{
    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"azcfg-dir-{Guid.NewGuid():N}"));
    private readonly DirectoryBrowser _browser = new();

    public DirectoryBrowserTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "beta"));
        Directory.CreateDirectory(Path.Combine(_root, "Alpha"));
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "alpine"));
        File.WriteAllText(Path.Combine(_root, "Alpha", "App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_root, "beta", "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "file.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task ListAsync_Folder_ReturnsSortedFoldersWithoutDotFoldersAndFiles()
    {
        var listing = await _browser.ListAsync(_root, default);

        Assert.Equal(_root, listing.Path);
        Assert.Equal(["Alpha", "alpine", "beta"], listing.Items.Select(i => i.Name));
        Assert.Equal(Path.GetDirectoryName(_root), listing.Parent);
    }

    [Fact]
    public async Task ListAsync_FlagsFoldersWithDotNetProjects()
    {
        var listing = await _browser.ListAsync(_root, default);

        Assert.Equal(["Alpha"], listing.Items.Where(i => i.HasDotNetProjects).Select(i => i.Name));
    }

    [Fact]
    public async Task ListAsync_SolutionFile_Flagged()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "beta", "All.slnx"), "<Solution />");

        Assert.Contains((await _browser.ListAsync(_root, default)).Items, i => i.Name == "beta" && i.HasDotNetProjects);
    }

    [Fact]
    public async Task ListAsync_MissingFolder_Empty() =>
        Assert.Empty((await _browser.ListAsync(Path.Combine(_root, "nope"), default)).Items);

    [Fact]
    public async Task ListAsync_NullPath_ReturnsRoots()
    {
        var listing = await _browser.ListAsync(null, default);

        Assert.Null(listing.Path);
        Assert.Null(listing.Parent);
        Assert.NotEmpty(listing.Items);
    }

    [Fact]
    public void Complete_PartialName_ReturnsMatchingFolders() =>
        Assert.Equal([Path.Combine(_root, "Alpha"), Path.Combine(_root, "alpine")], _browser.Complete(Path.Combine(_root, "al")));

    [Fact]
    public void Complete_TrailingSeparator_ListsChildrenUpToMax() =>
        Assert.Equal([Path.Combine(_root, "Alpha"), Path.Combine(_root, "alpine")], _browser.Complete(_root + Path.DirectorySeparatorChar, 2));

    [Fact]
    public void Complete_MissingFolder_Empty() =>
        Assert.Empty(_browser.Complete(Path.Combine(_root, "nope", "x")));
}
