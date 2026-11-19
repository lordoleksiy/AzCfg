using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class ConfigComparisonServiceTests
{
    private const string Canary = "CANARY-secret-value-123";
    private const string OtherCanary = "OTHER-secret-value-456";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly HmacSecretFingerprinter Fingerprinter = new(new byte[32]);
    private static readonly Uri DevSecret = new("https://dev-vault.vault.azure.net/secrets/Db--Password/v1");
    private static readonly Uri ProdSecret = new("https://prod-vault.vault.azure.net/secrets/Db--Password/v7");
    private static readonly Uri DevOtherSecret = new("https://dev-vault.vault.azure.net/secrets/Db--Other/v1");
    private static readonly Uri ProdOtherSecret = new("https://prod-vault.vault.azure.net/secrets/Db--Another/v1");
    private static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly ConfigEnvironment Prod = new("prod", new Uri("https://prod.azconfig.io"), null, true);
    private static readonly LocalTargetBinding Values = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding Secrets = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);

    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain(value), "etag");
    private static ConfigEntry Ref(string key, Uri uri) => new(key, ConfigScope.Service, new ConfigValue.KeyVaultRef(uri), "etag");

    private static ProjectBinding Binding(string[]? ignore = null, string? active = null, LocalTargetBinding? secretsTarget = null) =>
        new("/repo", "orders", Values, secretsTarget ?? Secrets, new Dictionary<string, Uri>(), active, ignore);

    private static ConfigComparisonService Create(
        ConfigEntry[] dev, ConfigEntry[]? prod = null, LocalValue[]? values = null, LocalValue[]? secrets = null, FakeUserStateStore? state = null)
    {
        var store = new FakeConfigStore([], new() { ["dev"] = dev, ["prod"] = prod ?? [] });
        var secretStore = new FakeSecretStore(new()
        {
            [DevSecret] = Canary,
            [ProdSecret] = Canary,
            [DevOtherSecret] = Canary,
            [ProdOtherSecret] = OtherCanary,
        });
        var targets = new FakeLocalTarget[]
        {
            new(LocalTargetKind.AppSettingsLocal) { Contents = values ?? [] },
            new(LocalTargetKind.UserSecrets) { Contents = secrets ?? [] },
        };
        return new(new SyncService(store, secretStore, state ?? new FakeUserStateStore(), Fingerprinter, targets));
    }

    private static CompareSide.Local Local => new();
    private static CompareSide.Environment Env(ConfigEnvironment env) => new(env);

    [Fact]
    public async Task LocalVsEnv_SecretWithSameValue_IsSameWithoutRevealingIt()
    {
        var sut = Create([Ref("Db:Password", DevSecret)], secrets: [new("Db:Password", Canary)]);

        var result = await sut.CompareAsync(Binding(), Local, Env(Dev), Ct);

        var item = Assert.Single(result.Items);
        Assert.Equal(ComparisonKind.Same, item.Kind);
        Assert.Equal(new ComparisonValue(true, null), item.Left);
        Assert.Equal(new ComparisonValue(true, null), item.Right);
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalVsEnv_SecretWithDifferentValue_IsDifferentWithoutRevealingEither()
    {
        var sut = Create([Ref("Db:Password", DevSecret)], secrets: [new("Db:Password", OtherCanary)]);

        var result = await sut.CompareAsync(Binding(), Local, Env(Dev), Ct);

        Assert.Equal(ComparisonKind.Different, Assert.Single(result.Items).Kind);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Canary, json, StringComparison.Ordinal);
        Assert.DoesNotContain(OtherCanary, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalVsEnv_PlainKeys_ShowValuesAndKinds()
    {
        var sut = Create([Plain("A", "1"), Plain("B", "x"), Plain("C", "r")], values: [new("A", "1"), new("B", "y"), new("D", "l")]);

        var result = await sut.CompareAsync(Binding(), Local, Env(Dev), Ct);

        Assert.Equal(
            [("A", ComparisonKind.Same), ("B", ComparisonKind.Different), ("C", ComparisonKind.OnlyRight), ("D", ComparisonKind.OnlyLeft)],
            result.Items.Select(i => (i.Key, i.Kind)));
        Assert.Equal(new ComparisonValue(false, "y"), result.Items[1].Left);
        Assert.Equal(new ComparisonValue(false, "x"), result.Items[1].Right);
    }

    [Fact]
    public async Task EnvVsLocal_FlipsSidesAndKinds()
    {
        var sut = Create([Plain("C", "r")], values: [new("D", "l")]);

        var result = await sut.CompareAsync(Binding(), Env(Dev), Local, Ct);

        Assert.Equal([("C", ComparisonKind.OnlyLeft), ("D", ComparisonKind.OnlyRight)], result.Items.Select(i => (i.Key, i.Kind)));
        Assert.Equal("r", result.Items[0].Left!.Plain);
        Assert.Equal("l", result.Items[1].Right!.Plain);
    }

    [Fact]
    public async Task LocalVsEnv_CountsIgnoredRemoteKeys()
    {
        var sut = Create([Plain("A", "1"), Plain("Logging:Level", "x")]);

        var result = await sut.CompareAsync(Binding(["Logging:*"]), Local, Env(Dev), Ct);

        Assert.Equal(["A"], result.Items.Select(i => i.Key));
        Assert.Equal(1, result.IgnoredCount);
    }

    [Fact]
    public async Task EnvVsEnv_SecretsEqualByNameAcrossVaults()
    {
        var sut = Create(
            [Ref("Db:Password", DevSecret), Ref("Db:Other", DevOtherSecret), Plain("A", "1")],
            [Ref("Db:Password", ProdSecret), Ref("Db:Other", ProdOtherSecret), Plain("A", "2")]);

        var result = await sut.CompareAsync(Binding(), Env(Dev), Env(Prod), Ct);

        Assert.Equal(
            [("A", ComparisonKind.Different), ("Db:Other", ComparisonKind.Different), ("Db:Password", ComparisonKind.Same)],
            result.Items.Select(i => (i.Key, i.Kind)));
        Assert.Equal(new ComparisonValue(true, null), result.Items[2].Left);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Canary, json, StringComparison.Ordinal);
        Assert.DoesNotContain(OtherCanary, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnvVsEnv_SkipsAndCountsIgnoredKeys()
    {
        var sut = Create([Plain("A", "1"), Plain("Logging:Level", "x")], [Plain("A", "1"), Plain("Logging:Level", "y")]);

        var result = await sut.CompareAsync(Binding(["Logging:*"]), Env(Dev), Env(Prod), Ct);

        Assert.Equal(["A"], result.Items.Select(i => i.Key));
        Assert.Equal(1, result.IgnoredCount);
    }

    [Fact]
    public async Task LocalVsEnv_SecretOnlyInSecretsTarget_IsHidden()
    {
        var sut = Create([], secrets: [new("Db:Password", Canary)]);

        var result = await sut.CompareAsync(Binding(), Local, Env(Dev), Ct);

        var item = Assert.Single(result.Items);
        Assert.Equal(ComparisonKind.OnlyLeft, item.Kind);
        Assert.Equal(new ComparisonValue(true, null), item.Left);
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalVsOtherEnv_SecretKnownToActiveSnapshot_IsHiddenWhenOtherEnvLacksIt()
    {
        var state = new FakeUserStateStore();
        state.Snapshots["dev"] = new(new Dictionary<string, StateItem>
        {
            ["Db:Password"] = new("Db:Password", ConfigScope.Service, new StateValue.Secret(DevSecret, Fingerprinter.Fingerprint(new SecretString(Canary)))),
        });
        var sut = Create([Ref("Db:Password", DevSecret)], prod: [], values: [new("Db:Password", Canary)], state: state);

        var result = await sut.CompareAsync(Binding(active: "dev", secretsTarget: Values), Local, Env(Prod), Ct);

        var item = Assert.Single(result.Items);
        Assert.Equal(ComparisonKind.OnlyLeft, item.Kind);
        Assert.Equal(new ComparisonValue(true, null), item.Left);
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalVsEnv_SecretsTargetKeyWithEqualPlainRemote_IsSameAndHidden()
    {
        var sut = Create([Plain("Db:Password", Canary)], secrets: [new("Db:Password", Canary)]);

        var result = await sut.CompareAsync(Binding(), Local, Env(Dev), Ct);

        var item = Assert.Single(result.Items);
        Assert.Equal(ComparisonKind.Same, item.Kind);
        Assert.Equal(new ComparisonValue(true, null), item.Left);
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalVsEnv_KeyOnlyInSnapshot_IsNotListed()
    {
        var state = new FakeUserStateStore();
        state.Snapshots["prod"] = new(new Dictionary<string, StateItem>
        {
            ["Old"] = new("Old", ConfigScope.Service, new StateValue.Plain("1")),
        });
        var sut = Create([], prod: [Plain("A", "1")], values: [new("A", "1")], state: state);

        var result = await sut.CompareAsync(Binding(active: "dev"), Local, Env(Prod), Ct);

        Assert.Equal(["A"], result.Items.Select(i => i.Key));
    }

    [Fact]
    public async Task LocalVsLocal_Throws()
    {
        var sut = Create([]);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CompareAsync(Binding(), Local, Local, Ct));
    }
}
