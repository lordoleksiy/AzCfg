using System.Text.Json.Nodes;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServiceReadRemoteConfigTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Uri SecretUri = new("https://vault.vault.azure.net/secrets/Db--Password/v1");
    private static readonly ConfigEnvironment StoreMode = new("dev", new Uri("https://dev.azconfig.io"), null, false, "Development");
    private static readonly ConfigEnvironment LabelMode = new("dev", new Uri("https://dev.azconfig.io"), "dev", false, "dev");
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("/repo", "orders", Target, Target, new Dictionary<string, Uri>());

    private static readonly ConfigEntry[] Entries =
    [
        new("A", ConfigScope.Service, new ConfigValue.Plain("service"), "e1"),
        new("A", ConfigScope.Global, new ConfigValue.Plain("global"), "e2"),
        new("B", ConfigScope.Global, new ConfigValue.Plain("g"), "e3", "dev"),
        new("B", ConfigScope.Global, new ConfigValue.Plain("g-unlabeled"), "e4"),
        new("C", ConfigScope.Service, new ConfigValue.Plain("dev"), "e5", "Development"),
        new("C", ConfigScope.Service, new ConfigValue.Plain("other"), "e6", "Other"),
        new("Db:Password", ConfigScope.Service, new ConfigValue.KeyVaultRef(SecretUri), "e7"),
    ];

    private sealed class CountingSecretStore : ISecretStore
    {
        public int Gets { get; private set; }

        public Task<string> GetAsync(Uri secretUri, CancellationToken ct)
        {
            Gets++;
            return Task.FromResult("AZCFG-TEST-SECRET-7f3a");
        }

        public Task<Uri> SetAsync(Uri vaultUri, string name, string value, CancellationToken ct) => throw new NotSupportedException();
    }

    private static (SyncService Service, CountingSecretStore Secrets) Create()
    {
        var secrets = new CountingSecretStore();
        return (new SyncService(new FakeConfigStore([], new() { ["dev"] = Entries }), secrets, new FakeUserStateStore(), new HmacSecretFingerprinter(new byte[32]), []), secrets);
    }

    [Fact]
    public async Task ReadRemoteConfigAsync_NeverReadsSecretValues()
    {
        var (service, secrets) = Create();

        var config = await service.ReadRemoteConfigAsync(Binding, StoreMode, Ct);

        Assert.Equal(0, secrets.Gets);
        Assert.Equal(new ConfigValue.KeyVaultRef(SecretUri), config.Entries.Single(e => e.Key == "Db:Password").Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadRemoteConfigAsync_EntriesMatchExportJson(bool labelMode)
    {
        var env = labelMode ? LabelMode : StoreMode;
        var (service, _) = Create();

        var config = await service.ReadRemoteConfigAsync(Binding, env, Ct);
        var exported = await service.ExportJsonAsync(Binding, env, Ct);

        Assert.Same(env, config.Environment);
        Assert.Equal(["A", "B", "C", "Db:Password"], config.Entries.Select(e => e.Key));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(exported), JsonProjection.ToEditJson(config.Entries)));
    }

    [Fact]
    public async Task ReadRemoteConfigAsync_KeepsLabelsAndETagsOfSelectedCopies()
    {
        var (service, _) = Create();

        var config = await service.ReadRemoteConfigAsync(Binding, StoreMode, Ct);

        Assert.Equal(new ConfigEntry("A", ConfigScope.Service, new ConfigValue.Plain("service"), "e1"), config.Entries[0]);
        Assert.Equal(new ConfigEntry("B", ConfigScope.Global, new ConfigValue.Plain("g-unlabeled"), "e4"), config.Entries[1]);
        Assert.Equal(new ConfigEntry("C", ConfigScope.Service, new ConfigValue.Plain("dev"), "e5", "Development"), config.Entries[2]);
    }

    [Fact]
    public async Task ReadRemoteConfigAsync_LabelMode_PrefersEnvironmentLabel()
    {
        var (service, _) = Create();

        var config = await service.ReadRemoteConfigAsync(Binding, LabelMode, Ct);

        Assert.Equal(new ConfigEntry("B", ConfigScope.Global, new ConfigValue.Plain("g"), "e3", "dev"), config.Entries[1]);
    }

    [Fact]
    public async Task ReadRemoteConfigAsync_ShadowedHoldsGlobalEntriesHiddenByServiceOverride()
    {
        var (service, _) = Create();

        var config = await service.ReadRemoteConfigAsync(Binding, StoreMode, Ct);

        Assert.Equal([new ConfigEntry("A", ConfigScope.Global, new ConfigValue.Plain("global"), "e2")], config.Shadowed);
    }

    [Fact]
    public async Task ReadRemoteConfigAsync_DuplicateLabels_Warns()
    {
        var (service, _) = Create();

        var config = await service.ReadRemoteConfigAsync(Binding, StoreMode, Ct);

        Assert.Equal(
            [(SyncWarningKind.DuplicateLabels, "B"), (SyncWarningKind.DuplicateLabels, "C")],
            config.Warnings.Select(w => (w.Kind, w.Key)));
    }
}
