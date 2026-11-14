using System.Text.Json.Nodes;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class SyncServiceExportPromoteTests
{
    private const string SecretText = "p@ss-value";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly Uri SecretUri = new("https://vault.vault.azure.net/secrets/Db--Password/v1");
    private static readonly ConfigEnvironment From = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly ConfigEnvironment To = new("stage", new Uri("https://stage.azconfig.io"), null, false);
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("/repo", "orders", Target, Target, new Dictionary<string, Uri>());

    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain(value));
    private static ConfigEntry Secret(string key) => new(key, ConfigScope.Service, new ConfigValue.KeyVaultRef(SecretUri));

    private static Task<string> Export(ConfigEntry[] from, ConfigEntry[] to) =>
        new SyncService(
            new FakeConfigStore([], new() { ["dev"] = from, ["stage"] = to }),
            new FakeSecretStore(new() { [SecretUri] = SecretText }),
            new FakeUserStateStore(),
            new HmacSecretFingerprinter(new byte[32]),
            []).ExportPromoteJsonAsync(Binding, From, To, Ct);

    private static bool JsonEquals(string expected, string actual) => JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual));

    [Fact]
    public async Task ExportPromoteJsonAsync_ExportsOnlyKeysMissingInTarget()
    {
        var json = await Export(
            [Plain("A", "1"), Plain("Logging:Level", "Info"), Plain("B:C", "dev")],
            [Plain("A", "stage"), Plain("B:C", "stage")]);

        Assert.True(JsonEquals("""{"Logging":{"Level":"Info"}}""", json));
    }

    [Fact]
    public async Task ExportPromoteJsonAsync_SecretHasNullValueAndSecretIsNotExposed()
    {
        var json = await Export([Secret("Db:Password")], []);

        Assert.True(JsonEquals("""{"Db":{"Password":{"@kv":"Db--Password","value":null}}}""", json));
        Assert.DoesNotContain(SecretText, json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportPromoteJsonAsync_IgnoresGlobal(bool globalInTarget)
    {
        var json = await Export(
            [Plain("G", "1", ConfigScope.Global), Plain("S", "2")],
            globalInTarget ? [Plain("G", "x", ConfigScope.Global)] : []);

        Assert.True(JsonEquals("""{"S":"2"}""", json));
    }

    [Fact]
    public async Task ExportPromoteJsonAsync_KeyOnlyGlobalInTarget_StillExported()
    {
        var json = await Export([Plain("A", "1")], [Plain("A", "g", ConfigScope.Global)]);

        Assert.True(JsonEquals("""{"A":"1"}""", json));
    }

    [Fact]
    public async Task ExportPromoteJsonAsync_NothingNew_ReturnsEmptyObject()
    {
        var json = await Export([Plain("A", "1")], [Plain("A", "2")]);

        Assert.Empty(JsonNode.Parse(json)!.AsObject());
    }
}
