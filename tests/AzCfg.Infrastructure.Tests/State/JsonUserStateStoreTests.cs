using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Infrastructure.State;

namespace AzCfg.Infrastructure.Tests.State;

public sealed class JsonUserStateStoreTests : IDisposable
{
    private const string Repo = "C:\\repos\\svc";
    private const string Other = "C:\\repos\\other";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "azcfg-tests-" + Guid.NewGuid().ToString("N"));
    private readonly JsonUserStateStore _store;

    public JsonUserStateStoreTests() => _store = new JsonUserStateStore(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ProjectBinding Binding(string repo = Repo, string service = "orders") => new(
        repo,
        service,
        new LocalTargetBinding(LocalTargetKind.AppSettingsLocal, "C:\\repos\\svc\\Api\\Api.csproj", LocalFileFormat.NestedJson),
        new LocalTargetBinding(LocalTargetKind.LocalSettings, "C:\\repos\\svc\\Fn\\Fn.csproj", LocalFileFormat.FlatDictionary),
        new Dictionary<string, Uri> { ["dev"] = new("https://kv-dev.vault.azure.net/") },
        "dev");

    private static ConfigState State() => new(new Dictionary<string, StateItem>
    {
        ["A:B"] = new("A:B", ConfigScope.Service, new StateValue.Plain("v")),
        ["Db:Pwd"] = new("Db:Pwd", ConfigScope.Global, new StateValue.Secret(new Uri("https://kv.vault.azure.net/secrets/db--pwd"), "fp")),
        ["Local:Only"] = new("Local:Only", ConfigScope.Service, new StateValue.Secret(null, "fp2")),
    });

    [Fact]
    public async Task Profile_RoundTrips()
    {
        var profile = new UserProfile([
            new("dev", new Uri("https://dev.azconfig.io"), null, false),
            new("prod", new Uri("https://prod.azconfig.io"), "l", true)]);

        await _store.SaveProfileAsync(profile, CancellationToken.None);
        var loaded = await _store.LoadProfileAsync(CancellationToken.None);

        Assert.Equal(profile.Environments, loaded!.Environments);
    }

    [Fact]
    public async Task Binding_RoundTrips()
    {
        var binding = Binding();

        await _store.SaveBindingAsync(binding, CancellationToken.None);
        var loaded = await _store.LoadBindingAsync(Repo, CancellationToken.None);

        Assert.Equal(binding.RepoPath, loaded!.RepoPath);
        Assert.Equal(binding.Service, loaded.Service);
        Assert.Equal(binding.ValuesTarget, loaded.ValuesTarget);
        Assert.Equal(binding.SecretsTarget, loaded.SecretsTarget);
        Assert.Equal(binding.VaultByEnvironment, loaded.VaultByEnvironment);
        Assert.Equal("dev", loaded.ActiveEnvironment);
    }

    [Fact]
    public async Task Binding_SeveralRepos_AreKeptSeparate_AndResaveReplaces()
    {
        await _store.SaveBindingAsync(Binding(), CancellationToken.None);
        await _store.SaveBindingAsync(Binding(Other, "billing"), CancellationToken.None);
        await _store.SaveBindingAsync(Binding(service: "orders2"), CancellationToken.None);

        Assert.Equal("orders2", (await _store.LoadBindingAsync(Repo, CancellationToken.None))!.Service);
        Assert.Equal("billing", (await _store.LoadBindingAsync(Other, CancellationToken.None))!.Service);
    }

    [Fact]
    public async Task Snapshot_RoundTrips_PerRepoAndEnvironment()
    {
        var state = State();

        await _store.SaveSnapshotAsync(Repo, "dev", state, CancellationToken.None);
        var loaded = await _store.LoadSnapshotAsync(Repo, "dev", CancellationToken.None);

        Assert.Equal(state.Items.Keys, loaded!.Items.Keys);
        Assert.All(state.Items, p => Assert.Equal(p.Value, loaded.Items[p.Key]));
        Assert.Null(await _store.LoadSnapshotAsync(Repo, "stage", CancellationToken.None));
        Assert.Null(await _store.LoadSnapshotAsync(Other, "dev", CancellationToken.None));
    }

    [Fact]
    public async Task MissingFiles_LoadAsNull()
    {
        Assert.Null(await _store.LoadProfileAsync(CancellationToken.None));
        Assert.Null(await _store.LoadBindingAsync(Repo, CancellationToken.None));
        Assert.Null(await _store.LoadSnapshotAsync(Repo, "dev", CancellationToken.None));
    }

    [Fact]
    public async Task FingerprintKey_Is32Bytes_AndStableAcrossCallsAndInstances()
    {
        var first = await _store.GetOrCreateFingerprintKeyAsync(CancellationToken.None);
        var second = await _store.GetOrCreateFingerprintKeyAsync(CancellationToken.None);
        var other = await new JsonUserStateStore(_root).GetOrCreateFingerprintKeyAsync(CancellationToken.None);

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.Equal(first, other);
    }

    [Fact]
    public async Task Save_LeavesNoTempFiles()
    {
        await _store.SaveProfileAsync(new UserProfile([]), CancellationToken.None);
        await _store.SaveBindingAsync(Binding(), CancellationToken.None);
        await _store.SaveSnapshotAsync(Repo, "dev", State(), CancellationToken.None);

        Assert.DoesNotContain(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories), f => f.EndsWith(".tmp"));
    }
}
