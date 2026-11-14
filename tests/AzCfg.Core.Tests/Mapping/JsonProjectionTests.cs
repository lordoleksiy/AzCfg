using System.Text.Json.Nodes;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Mapping;

public class JsonProjectionTests
{
    private static JsonObject Nest(params (string Key, string Value)[] values) =>
        JsonProjection.ToNestedJson(values.Select(v => new LocalValue(v.Key, v.Value)));

    private static IEnumerable<LocalValue> LazyValues()
    {
        yield return new LocalValue("A:B", "1");
    }

    [Fact]
    public void ToNestedJson_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => JsonProjection.ToNestedJson(null!));
    }

    [Fact]
    public void ToNestedJson_EmptyInput_ReturnsEmptyObject()
    {
        var result = JsonProjection.ToNestedJson(Array.Empty<LocalValue>());

        Assert.Empty(result);
        Assert.Equal("{}", result.ToJsonString());
    }

    [Fact]
    public void ToNestedJson_SingleSegmentKey_IsTopLevelProperty()
    {
        var result = Nest(("Simple", "v"));

        Assert.Equal("""{"Simple":"v"}""", result.ToJsonString());
    }

    [Fact]
    public void ToNestedJson_ColonSeparatedKey_BecomesNestedObjects()
    {
        var result = Nest(("A:B:C", "v"));

        Assert.Equal("""{"A":{"B":{"C":"v"}}}""", result.ToJsonString());
    }

    [Fact]
    public void ToNestedJson_SharedPrefixes_AreMergedIntoOneObject()
    {
        var result = Nest(
            ("ConnectionStrings:Crm", "crm"),
            ("ConnectionStrings:Dataverse", "dv"),
            ("Logging:LogLevel:Default", "Information"));

        Assert.Equal(
            """{"ConnectionStrings":{"Crm":"crm","Dataverse":"dv"},"Logging":{"LogLevel":{"Default":"Information"}}}""",
            result.ToJsonString());
    }

    [Fact]
    public void ToNestedJson_ValuesAreAlwaysJsonStrings()
    {
        var result = Nest(("Features:UseNewPricing", "true"), ("Limits:Max", "42"), ("Empty", ""));

        Assert.Equal(
            """{"Empty":"","Features":{"UseNewPricing":"true"},"Limits":{"Max":"42"}}""",
            result.ToJsonString());
    }

    [Fact]
    public void ToNestedJson_PropertiesAreOrderedOrdinalAtEveryLevel_RegardlessOfInputOrder()
    {
        var result = Nest(
            ("b", "1"),
            ("A:z", "2"),
            ("a", "3"),
            ("A:B", "4"),
            ("A-C", "5"));

        Assert.Equal("""{"A":{"B":"4","z":"2"},"A-C":"5","a":"3","b":"1"}""", result.ToJsonString());
    }

    [Fact]
    public void ToNestedJson_KeysAreCaseSensitive()
    {
        var result = Nest(("A:x", "1"), ("a:x", "2"));

        Assert.Equal("""{"A":{"x":"1"},"a":{"x":"2"}}""", result.ToJsonString());
    }

    [Fact]
    public void ToNestedJson_ValuesWithSpecialCharacters_AreKeptVerbatim()
    {
        var value = "Server=x;Password=\"p:a{s}s\"";

        var result = Nest(("Db:Conn", value));

        Assert.Equal(value, result["Db"]!["Conn"]!.GetValue<string>());
    }

    [Fact]
    public void ToNestedJson_DuplicateKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => Nest(("A:B", "1"), ("A:B", "2")));
    }

    [Theory]
    [InlineData("A", "A:B")]
    [InlineData("A:B", "A")]
    [InlineData("A:B", "A:B:C")]
    public void ToNestedJson_KeyIsBothValueAndParentOfAnotherKey_Throws(string first, string second)
    {
        Assert.Throws<ArgumentException>(() => Nest((first, "1"), (second, "2")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A::B")]
    [InlineData(":A")]
    [InlineData("A:")]
    public void ToNestedJson_InvalidKey_Throws(string key)
    {
        Assert.ThrowsAny<ArgumentException>(() => Nest((key, "v")));
    }

    [Fact]
    public void ToNestedJson_NullElement_Throws()
    {
        Assert.Throws<ArgumentException>(() => JsonProjection.ToNestedJson(new LocalValue[] { null! }));
    }

    [Fact]
    public void ToNestedJson_NullValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => JsonProjection.ToNestedJson(new[] { new LocalValue("A", null!) }));
    }

    [Fact]
    public void ToNestedJson_AcceptsLazyEnumerable()
    {
        var result = JsonProjection.ToNestedJson(LazyValues());

        Assert.Equal("""{"A":{"B":"1"}}""", result.ToJsonString());
    }

    [Fact]
    public void ToNestedJson_ReturnsIndependentInstances()
    {
        var values = new[] { new LocalValue("A:B", "1") };

        var first = JsonProjection.ToNestedJson(values);
        first["A"]!.AsObject()["B"] = "changed";

        Assert.Equal("""{"A":{"B":"1"}}""", JsonProjection.ToNestedJson(values).ToJsonString());
    }

    private static ConfigEntry Plain(string key, string value, ConfigScope scope = ConfigScope.Service) =>
        new(key, scope, new ConfigValue.Plain(value));

    private static ConfigEntry Kv(string key, string uri, ConfigScope scope = ConfigScope.Service) =>
        new(key, scope, new ConfigValue.KeyVaultRef(new Uri(uri)));

    [Fact]
    public void ToEditJson_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => JsonProjection.ToEditJson(null!));
    }

    [Fact]
    public void ToEditJson_EmptyInput_ReturnsEmptyObject()
    {
        Assert.Equal("{}", JsonProjection.ToEditJson([]).ToJsonString());
    }

    [Fact]
    public void ToEditJson_ArchitectureExample_ProducesExpectedJson()
    {
        var result = JsonProjection.ToEditJson(
        [
            Kv("ConnectionStrings:Crm", "https://kv.vault.azure.net/secrets/crm-api-service--ConnectionStrings--Crm/abc123"),
            Plain("Features:UseNewPricing", "true"),
            Plain("Logging:Level", "Information", ConfigScope.Global),
        ]);

        Assert.Equal(
            """{"ConnectionStrings":{"Crm":{"@kv":"crm-api-service--ConnectionStrings--Crm"}},"Features":{"UseNewPricing":"true"},"@global":{"Logging":{"Level":"Information"}}}""",
            result.ToJsonString());
    }

    [Theory]
    [InlineData("https://kv.vault.azure.net/secrets/my-secret")]
    [InlineData("https://kv.vault.azure.net/secrets/my-secret/0123abcd")]
    public void ToEditJson_KeyVaultRef_UsesSecretNameWithoutVersion(string uri)
    {
        var result = JsonProjection.ToEditJson([Kv("A:B", uri)]);

        Assert.Equal("""{"A":{"B":{"@kv":"my-secret"}}}""", result.ToJsonString());
    }

    [Fact]
    public void ToEditJson_ResolvedSecret_NeverExposesValue()
    {
        var secret = new ConfigValue.ResolvedSecret(
            new Uri("https://kv.vault.azure.net/secrets/my-secret/v1"), new SecretString("s3cr3t-value"));

        var result = JsonProjection.ToEditJson([new ConfigEntry("A", ConfigScope.Service, secret)]);

        Assert.Equal("""{"A":{"@kv":"my-secret"}}""", result.ToJsonString());
    }

    [Fact]
    public void ToEditJson_OnlyServiceEntries_HasNoGlobalProperty()
    {
        var result = JsonProjection.ToEditJson([Plain("A", "1")]);

        Assert.False(result.ContainsKey("@global"));
    }

    [Fact]
    public void ToEditJson_OnlyGlobalEntries_HasOnlyGlobalProperty()
    {
        var result = JsonProjection.ToEditJson([Plain("A:B", "1", ConfigScope.Global)]);

        Assert.Equal("""{"@global":{"A":{"B":"1"}}}""", result.ToJsonString());
    }

    [Fact]
    public void ToEditJson_PropertiesAreOrderedOrdinal_RegardlessOfInputOrder()
    {
        var result = JsonProjection.ToEditJson([Plain("b", "1"), Plain("A:z", "2"), Plain("a", "3"), Plain("A:B", "4")]);

        Assert.Equal("""{"A":{"B":"4","z":"2"},"a":"3","b":"1"}""", result.ToJsonString());
    }

    [Theory]
    [InlineData("A", "A:B")]
    [InlineData("A:B", "A")]
    public void ToEditJson_KeyIsBothValueAndParent_Throws(string first, string second)
    {
        Assert.Throws<ArgumentException>(
            () => JsonProjection.ToEditJson([Kv(first, "https://kv.vault.azure.net/secrets/s"), Plain(second, "2")]));
    }

    [Fact]
    public void ToEditJson_NullElement_Throws()
    {
        Assert.Throws<ArgumentException>(() => JsonProjection.ToEditJson([null!]));
    }

    private static readonly HmacSecretFingerprinter Fingerprinter = new(Enumerable.Repeat((byte)1, 32).ToArray());
    private static readonly Uri CrmUri = new("https://kv.vault.azure.net/secrets/crm-api--Crm");
    private static readonly StateValue.Secret KnownCrm = new(CrmUri, "known-fp");

    private static ParsedEditJson Parse(string json, params (string Name, StateValue.Secret Secret)[] known) =>
        JsonProjection.FromEditJson(json, Fingerprinter, known.ToDictionary(k => k.Name, k => k.Secret));

    [Fact]
    public void FromEditJson_RoundTripsToEditJson()
    {
        var entries = new[]
        {
            Plain("Features:Flag", "true"),
            Plain("Logging:Level", "Information", ConfigScope.Global),
            Kv("ConnectionStrings:Crm", CrmUri.ToString()),
        };

        var parsed = Parse(JsonProjection.ToEditJson(entries).ToJsonString(), ("crm-api--Crm", KnownCrm));

        Assert.Equal(
            [
                new StateItem("ConnectionStrings:Crm", ConfigScope.Service, KnownCrm),
                new StateItem("Features:Flag", ConfigScope.Service, new StateValue.Plain("true")),
                new StateItem("Logging:Level", ConfigScope.Global, new StateValue.Plain("Information")),
            ],
            parsed.State.Items.Values.OrderBy(i => i.Key, StringComparer.Ordinal));
        Assert.Empty(parsed.NewSecrets);
    }

    [Fact]
    public void FromEditJson_GlobalSection_GetsGlobalScope()
    {
        var parsed = Parse("""{"A":"1","@global":{"B":{"C":"2"}}}""");

        Assert.Equal(ConfigScope.Service, parsed.State.Items["A"].Scope);
        Assert.Equal(ConfigScope.Global, parsed.State.Items["B:C"].Scope);
    }

    [Theory]
    [InlineData("""{"A":true}""", "true")]
    [InlineData("""{"A":42}""", "42")]
    [InlineData("""{"A":1.50}""", "1.50")]
    [InlineData("""{"A":"x"}""", "x")]
    public void FromEditJson_ScalarsBecomeStrings(string json, string expected) =>
        Assert.Equal(new StateValue.Plain(expected), Parse(json).State.Items["A"].Value);

    [Fact]
    public void FromEditJson_KvWithValue_IsReturnedAsNewSecretAndFingerprinted()
    {
        var parsed = Parse("""{"@global":{"Db":{"@kv":"db--Pwd","value":"s3cret"}}}""");

        var secret = Assert.Single(parsed.NewSecrets);
        Assert.Equal(("Db", ConfigScope.Global, "db--Pwd", new SecretString("s3cret")), (secret.Key, secret.Scope, secret.Name, secret.Value));
        Assert.Equal(
            new StateValue.Secret(null, Fingerprinter.Fingerprint(new SecretString("s3cret"))),
            parsed.State.Items["Db"].Value);
    }

    [Fact]
    public void FromEditJson_KvWithoutValue_UsesKnownSecretOrNullUri()
    {
        var parsed = Parse("""{"A":{"@kv":"crm-api--Crm"},"B":{"@kv":"other"}}""", ("crm-api--Crm", KnownCrm));

        Assert.Equal(KnownCrm, parsed.State.Items["A"].Value);
        Assert.Equal(new StateValue.Secret(null, ""), parsed.State.Items["B"].Value);
        Assert.Empty(parsed.NewSecrets);
    }

    [Theory]
    [InlineData("""{"Db":{"Pwd":{"@kv":"n","value":null}}}""")]
    [InlineData("""{"Db":{"Pwd":null}}""")]
    [InlineData("""{"Db":{"Pwd":[1]}}""")]
    public void FromEditJson_NullOrArray_ThrowsWithKeyName(string json)
    {
        var ex = Assert.Throws<ArgumentException>(() => Parse(json));

        Assert.Contains("'Db:Pwd'", ex.Message);
        Assert.Contains("remove the key", ex.Message);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    public void FromEditJson_InvalidJson_Throws(string json) =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse(json));

    [Theory]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    public void FromEditJson_NonObjectRoot_Throws(string json) =>
        Assert.Throws<ArgumentException>(() => Parse(json));
}
