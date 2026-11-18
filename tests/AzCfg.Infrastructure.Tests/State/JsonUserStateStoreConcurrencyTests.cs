using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Infrastructure.State;

namespace AzCfg.Infrastructure.Tests.State;

public sealed class JsonUserStateStoreConcurrencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "azcfg-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveBindingAsync_FiftyParallelSavesOfDifferentRepos_AllPersisted()
    {
        var store = new JsonUserStateStore(_root);
        var repos = Enumerable.Range(0, 50).Select(i => Path.Combine(_root, "repos", "r" + i)).ToList();

        await Task.WhenAll(repos.Select(r => Task.Run(() => store.SaveBindingAsync(Binding(r), CancellationToken.None))));

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "projects.json")));
        Assert.Equal(50, doc.RootElement.EnumerateObject().Count());
        foreach (var repo in repos)
        {
            Assert.NotNull(await store.LoadBindingAsync(repo, CancellationToken.None));
        }
    }

    [Fact]
    public async Task SaveBindingAsync_CancelledToken_Throws()
    {
        var store = new JsonUserStateStore(_root);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveBindingAsync(Binding(Path.Combine(_root, "r")), cts.Token));
    }

    [Fact]
    public async Task GetOrCreateFingerprintKeyAsync_Parallel_ReturnsSameKey()
    {
        var store = new JsonUserStateStore(_root);

        var keys = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => store.GetOrCreateFingerprintKeyAsync(CancellationToken.None))));

        Assert.All(keys, k => Assert.Equal(keys[0], k));
    }

    private static ProjectBinding Binding(string repo) => new(
        repo,
        "svc",
        new LocalTargetBinding(LocalTargetKind.AppSettingsLocal, Path.Combine(repo, "A.csproj"), LocalFileFormat.NestedJson),
        new LocalTargetBinding(LocalTargetKind.UserSecrets, Path.Combine(repo, "A.csproj"), LocalFileFormat.NestedJson),
        new Dictionary<string, Uri>());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
