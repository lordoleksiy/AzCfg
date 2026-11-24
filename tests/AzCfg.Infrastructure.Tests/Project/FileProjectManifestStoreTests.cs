using AzCfg.Core.Manifest;
using AzCfg.Infrastructure.Project;

namespace AzCfg.Infrastructure.Tests.Project;

public sealed class FileProjectManifestStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "azcfg-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly FileProjectManifestStore _store = new();

    public FileProjectManifestStoreTests()
    {
        _repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(_repo, ".git"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ProjectManifest Manifest(string service = "orders-api") => new(
        1,
        service,
        new ManifestTarget("appsettings-local", "src/Orders.Api/Orders.Api.csproj"),
        new ManifestTarget("user-secrets", "src/Orders.Api/Orders.Api.csproj"),
        new Dictionary<string, Uri> { ["dev"] = new("https://orders-dev-kv.vault.azure.net/") });

    private string Folder(params string[] parts)
    {
        var path = Path.Combine([_repo, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task FindAsync_FromNestedFolder_ReturnsNearestManifestAndPath()
    {
        var file = Path.Combine(_repo, "azcfg.json");
        await _store.SaveAsync(file, Manifest(), CancellationToken.None);

        var found = await _store.FindAsync(Folder("src", "Orders.Api"), CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(file, found.FilePath);
        Assert.Equal("orders-api", found.Manifest.Service);
    }

    [Fact]
    public async Task FindAsync_PrefersTheClosestManifest()
    {
        await _store.SaveAsync(Path.Combine(_repo, "azcfg.json"), Manifest("root"), CancellationToken.None);
        var service = Folder("services", "orders");
        await _store.SaveAsync(Path.Combine(service, "azcfg.json"), Manifest("orders"), CancellationToken.None);

        var found = await _store.FindAsync(Folder("services", "orders", "src"), CancellationToken.None);

        Assert.Equal("orders", found!.Manifest.Service);
        Assert.Equal(Path.Combine(service, "azcfg.json"), found.FilePath);
    }

    [Fact]
    public async Task FindAsync_FromProjectFile_SearchesFromItsFolder()
    {
        var project = Path.Combine(Folder("src", "Api"), "Api.csproj");
        await File.WriteAllTextAsync(project, "<Project />");
        await _store.SaveAsync(Path.Combine(_repo, "src", "Api", "azcfg.json"), Manifest(), CancellationToken.None);

        var found = await _store.FindAsync(project, CancellationToken.None);

        Assert.Equal(Path.Combine(_repo, "src", "Api", "azcfg.json"), found!.FilePath);
    }

    [Fact]
    public async Task FindAsync_StopsAtGitRoot_IgnoresManifestAboveIt()
    {
        await _store.SaveAsync(Path.Combine(_root, "azcfg.json"), Manifest(), CancellationToken.None);

        Assert.Null(await _store.FindAsync(Folder("src"), CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_GitFileMarksRoot_LikeAWorktree()
    {
        var worktree = Path.Combine(_root, "wt");
        Directory.CreateDirectory(Path.Combine(worktree, "src"));
        await File.WriteAllTextAsync(Path.Combine(worktree, ".git"), "gitdir: elsewhere");
        await _store.SaveAsync(Path.Combine(_root, "azcfg.json"), Manifest(), CancellationToken.None);

        Assert.Null(await _store.FindAsync(Path.Combine(worktree, "src"), CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_NoManifest_ReturnsNull() =>
        Assert.Null(await _store.FindAsync(Folder("src"), CancellationToken.None));

    [Fact]
    public async Task SaveAsync_ThenFind_RoundTrips()
    {
        var file = Path.Combine(_repo, "azcfg.json");
        var manifest = Manifest();

        await _store.SaveAsync(file, manifest, CancellationToken.None);
        var found = (await _store.FindAsync(_repo, CancellationToken.None))!.Manifest;

        Assert.Equal(manifest.SchemaVersion, found.SchemaVersion);
        Assert.Equal(manifest.Service, found.Service);
        Assert.Equal(manifest.Values, found.Values);
        Assert.Equal(manifest.Secrets, found.Secrets);
        Assert.Equal(manifest.Vaults, found.Vaults);
        Assert.Empty(Directory.GetFiles(_repo, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingFile()
    {
        var file = Path.Combine(_repo, "azcfg.json");
        await _store.SaveAsync(file, Manifest("old"), CancellationToken.None);

        await _store.SaveAsync(file, Manifest("new"), CancellationToken.None);

        Assert.Equal("new", (await _store.FindAsync(_repo, CancellationToken.None))!.Manifest.Service);
    }

    [Fact]
    public async Task SaveAsync_InvalidManifest_ThrowsWithPathAndWritesNothing()
    {
        var file = Path.Combine(_repo, "azcfg.json");

        var e = await Assert.ThrowsAsync<ManifestException>(() => _store.SaveAsync(file, Manifest(service: ""), CancellationToken.None));

        Assert.Equal(file, e.FilePath);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task FindAsync_CorruptedFile_ThrowsUnreadableWithPath()
    {
        var file = Path.Combine(_repo, "azcfg.json");
        await File.WriteAllTextAsync(file, "{ \"schemaVersion\": 1, ");

        var e = await Assert.ThrowsAsync<ManifestException>(() => _store.FindAsync(_repo, CancellationToken.None));

        Assert.Equal(ManifestErrorKind.Unreadable, e.Kind);
        Assert.Equal(file, e.FilePath);
        Assert.Contains(file, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindAsync_InvalidSchema_ThrowsSchemaWithPath()
    {
        var file = Path.Combine(_repo, "azcfg.json");
        await File.WriteAllTextAsync(file, """
            {
              "schemaVersion": 1,
              "service": "orders-api",
              "values":  { "target": "appsettings-local", "project": "Api/Api.csproj" },
              "secrets": { "target": "user-secrets", "project": "Api/Api.csproj" },
              "password": "x"
            }
            """);

        var e = await Assert.ThrowsAsync<ManifestException>(() => _store.FindAsync(_repo, CancellationToken.None));

        Assert.Equal(ManifestErrorKind.Schema, e.Kind);
        Assert.Equal(file, e.FilePath);
    }
}
