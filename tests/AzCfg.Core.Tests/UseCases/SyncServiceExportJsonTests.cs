using System.Text.Json.Nodes;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServiceExportJsonTests
{
    private const string SecretText = "p@ss-value";

    private static readonly Uri SecretUri = new("https://vault.vault.azure.net/secrets/Db--Password/v1");
    private static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("/repo", "orders", Target, Target, new Dictionary<string, Uri>());

    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain(value));

    private static ConfigEntry Secret(string key, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.KeyVaultRef(SecretUri));

    private static Task<string> Export(params ConfigEntry[] entries) =>
        new SyncService(
            new FakeConfigStore([], new() { ["dev"] = entries }),
            new FakeSecretStore(new() { [SecretUri] = SecretText }),
            new FakeUserStateStore(),
            new HmacSecretFingerprinter(new byte[32]),
            []).ExportJsonAsync(Binding, Dev, CancellationToken.None);

    private static bool JsonEquals(string expected, string actual) => JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual));

    [Fact]
    public async Task ExportJsonAsync_ServiceAtRoot_GlobalUnderGlobalNode()
    {
        var json = await Export(Plain("Db:Host", "h"), Plain("Logging:Level", "Info", ConfigScope.Global));

        Assert.True(JsonEquals("""{"Db":{"Host":"h"},"@global":{"Logging":{"Level":"Info"}}}""", json));
    }

    [Fact]
    public async Task ExportJsonAsync_ServiceOverridesGlobal_OnlyServiceValueAndRoundTrips()
    {
        var json = await Export(Plain("Level", "s"), Plain("Level", "g", ConfigScope.Global), Plain("Other", "o", ConfigScope.Global));

        Assert.True(JsonEquals("""{"Level":"s","@global":{"Other":"o"}}""", json));
        JsonProjection.FromEditJson(json, new HmacSecretFingerprinter(new byte[32]), new Dictionary<string, StateValue.Secret>());
    }

    [Fact]
    public async Task ExportJsonAsync_SecretIsReferenceOnly_ValueNeverExposed()
    {
        var json = await Export(Secret("Db:Password"), Secret("Shared:Password", ConfigScope.Global));

        Assert.True(JsonEquals("""{"Db":{"Password":{"@kv":"Db--Password"}},"@global":{"Shared":{"Password":{"@kv":"Db--Password"}}}}""", json));
        Assert.DoesNotContain(SecretText, json);
    }

    [Fact]
    public async Task ExportJsonAsync_NoEntries_ReturnsEmptyObject() =>
        Assert.Empty(JsonNode.Parse(await Export())!.AsObject());
}
