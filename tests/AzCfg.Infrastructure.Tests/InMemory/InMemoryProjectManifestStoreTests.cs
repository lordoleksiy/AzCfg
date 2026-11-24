using AzCfg.Core.Manifest;
using AzCfg.Infrastructure.InMemory;

namespace AzCfg.Infrastructure.Tests.InMemory;

public sealed class InMemoryProjectManifestStoreTests
{
    private static readonly string Repo = Path.Combine(Path.GetTempPath(), "repo");

    private readonly InMemoryProjectManifestStore _store = new();

    private static ProjectManifest Manifest() => new(
        1,
        "orders-api",
        new ManifestTarget("appsettings-local", "Api/Api.csproj"),
        new ManifestTarget("user-secrets", "Api/Api.csproj"),
        null);

    [Fact]
    public async Task SaveThenFind_FromNestedPath_RoundTripsAsJson()
    {
        var file = Path.Combine(Repo, "azcfg.json");
        await _store.SaveAsync(file, Manifest(), CancellationToken.None);

        var found = await _store.FindAsync(Path.Combine(Repo, "Api"), CancellationToken.None);

        Assert.Equal(file, found!.FilePath);
        Assert.Equal("orders-api", found.Manifest.Service);
        Assert.Contains("\"service\": \"orders-api\"", _store.Json(file), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindAsync_StopsAtGitRoot()
    {
        await _store.SaveAsync(Path.Combine(Path.GetDirectoryName(Repo)!, "azcfg.json"), Manifest(), CancellationToken.None);
        _store.GitRoots.Add(Repo);

        Assert.Null(await _store.FindAsync(Path.Combine(Repo, "Api"), CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_CorruptedJson_ThrowsWithPath()
    {
        var file = Path.Combine(Repo, "azcfg.json");
        _store.SetJson(file, "{");

        var e = await Assert.ThrowsAsync<ManifestException>(() => _store.FindAsync(Repo, CancellationToken.None));

        Assert.Equal(ManifestErrorKind.Unreadable, e.Kind);
        Assert.Equal(file, e.FilePath);
    }
}
