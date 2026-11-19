using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServiceStatusTests
{
    private const string BaseSecret = "base-secret";
    private const string Canary = "CANARY-secret-value-9f3a";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly HmacSecretFingerprinter Fingerprinter = new(new byte[32]);
    private static readonly Uri SecretUri = new("https://vault.vault.azure.net/secrets/Db--Password/v1");
    private static readonly ConfigEnvironment Env = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly LocalTargetBinding Values = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);

    private static ConfigEntry Plain(string key, string value) => new(key, ConfigScope.Service, new ConfigValue.Plain(value), "etag");
    private static StateItem PlainItem(string key, string value) => new(key, ConfigScope.Service, new StateValue.Plain(value));
    private static ConfigState State(params StateItem[] items) => new(items.ToDictionary(i => i.Key, StringComparer.Ordinal));

    private static ProjectBinding Binding(string? active = "dev", string[]? ignore = null) =>
        new("/repo", "orders", Values, Values, new Dictionary<string, Uri>(), ActiveEnvironment: active, Ignore: ignore);

    private static SyncService Create(ConfigEntry[] entries, LocalValue[] local, ConfigState snapshot)
    {
        var state = new FakeUserStateStore();
        state.Snapshots["dev"] = snapshot;
        var target = new FakeLocalTarget(LocalTargetKind.AppSettingsLocal) { Contents = local };
        var secretValues = entries.Select(e => e.Value).OfType<ConfigValue.KeyVaultRef>().DistinctBy(r => r.SecretUri).ToDictionary(r => r.SecretUri, _ => BaseSecret);
        return new(new FakeConfigStore([], new() { ["dev"] = entries }), new FakeSecretStore(secretValues), state, Fingerprinter, [target]);
    }

    [Fact]
    public async Task StatusAsync_EveryDiffKindLandsInItsCounter()
    {
        // base: U LM LD RM RD C; remote: U LM LD RM2 C3 RA; local: U LM2 LA C2 RM RD
        var service = Create(
            [Plain("U", "1"), Plain("LM", "1"), Plain("LD", "1"), Plain("RM", "2"), Plain("C", "3"), Plain("RA", "x")],
            [new("U", "1"), new("LM", "2"), new("LA", "y"), new("C", "2"), new("RM", "1"), new("RD", "1")],
            State(PlainItem("U", "1"), PlainItem("LM", "1"), PlainItem("LD", "1"), PlainItem("RM", "1"), PlainItem("RD", "1"), PlainItem("C", "1")));

        var summary = await service.StatusAsync(Binding(), Env, Ct);

        Assert.Equal("dev", summary.Environment);
        Assert.True(summary.IsActive);
        Assert.Equal(3, summary.LocalChanges);
        Assert.Equal(3, summary.RemoteChanges);
        Assert.Equal(["C"], summary.Conflicts);
        Assert.Equal(0, summary.IgnoredCount);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public async Task StatusAsync_BlockedKeys_AreLocalChangesAndConflicts_Ordered()
    {
        var service = Create(
            [Plain("A", "r"), Plain("B", "1"), Plain("C", "3"), Plain("D", "1")],
            [new("A", "r"), new("B", "2"), new("C", "2"), new("E", "new")],
            State(PlainItem("A", "r"), PlainItem("B", "1"), PlainItem("C", "1"), PlainItem("D", "1")));

        var summary = await service.StatusAsync(Binding(), Env, Ct);

        Assert.Equal(["B", "C", "D", "E"], summary.BlockedKeys);
        Assert.Equal(["C"], summary.Conflicts);
    }

    [Fact]
    public async Task StatusAsync_Clean_AllZero()
    {
        var service = Create([Plain("A", "1")], [new("A", "1")], State(PlainItem("A", "1")));

        var summary = await service.StatusAsync(Binding(), Env, Ct);

        Assert.Equal((0, 0), (summary.LocalChanges, summary.RemoteChanges));
        Assert.Empty(summary.Conflicts);
        Assert.Empty(summary.BlockedKeys);
    }

    [Fact]
    public async Task StatusAsync_IgnoredKeys_AreCountedNotListed()
    {
        var service = Create([Plain("A", "1")], [new("A", "1"), new("Ign:X", "x"), new("Ign:Y", "y")], State(PlainItem("A", "1")));

        var summary = await service.StatusAsync(Binding(ignore: ["Ign:*"]), Env, Ct);

        Assert.Equal(2, summary.IgnoredCount);
        Assert.Equal(0, summary.LocalChanges);
        Assert.Empty(summary.BlockedKeys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("prod")]
    public async Task StatusAsync_NotActiveEnvironment_ReportsNoCounts(string? active)
    {
        var service = Create([Plain("A", "1"), Plain("R", "x")], [new("A", "2")], State(PlainItem("A", "1")));

        var summary = await service.StatusAsync(Binding(active), Env, Ct);

        Assert.False(summary.IsActive);
        Assert.Equal("dev", summary.Environment);
        Assert.Equal((0, 0, 0), (summary.LocalChanges, summary.RemoteChanges, summary.IgnoredCount));
        Assert.Empty(summary.Conflicts);
        Assert.Empty(summary.BlockedKeys);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public async Task StatusAsync_ActiveEnvironmentMatchedIgnoringCase()
    {
        var service = Create([Plain("A", "1")], [new("A", "1")], State(PlainItem("A", "1")));

        Assert.True((await service.StatusAsync(Binding("DEV"), Env, Ct)).IsActive);
    }

    [Fact]
    public async Task StatusAsync_SecretValueNeverAppearsInSerializedSummary()
    {
        var secretItem = new StateItem("Db:Password", ConfigScope.Service, new StateValue.Secret(SecretUri, Fingerprinter.Fingerprint(new SecretString(BaseSecret))));
        var service = Create(
            [new ConfigEntry("Db:Password", ConfigScope.Service, new ConfigValue.KeyVaultRef(SecretUri), "etag")],
            [new("Db:Password", Canary)],
            State(secretItem));

        var summary = await service.StatusAsync(Binding(), Env, Ct);

        Assert.Equal(["Db:Password"], summary.BlockedKeys);
        var json = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain(Canary, json);
        Assert.DoesNotContain(BaseSecret, json);
    }
}
