using System.Text.Json;
using System.Text.Json.Nodes;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Mapping;

public class EditJsonPatchTests
{
    private const string Sentinel = "AZCFG-TEST-SECRET-7f3a";

    private static readonly Uri SecretUri = new("https://vault.vault.azure.net/secrets/orders--Db--Password/0123abcd");

    private static ScopedKey Service(string key) => new(ConfigScope.Service, key);
    private static ScopedKey Global(string key) => new(ConfigScope.Global, key);

    private static void AssertJson(string expected, string actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), actual);

    // SetPlain

    [Fact]
    public void SetPlain_IntoEmpty_CreatesNestedObjects_Indented()
    {
        var result = EditJsonPatch.SetPlain("{}", Service("Db:Host"), "localhost");

        Assert.Equal(
            """
            {
              "Db": {
                "Host": "localhost"
              }
            }
            """.ReplaceLineEndings(), result.ReplaceLineEndings());
    }

    [Fact]
    public void SetPlain_IntoExistingParent_AppendsAndKeepsOrder()
    {
        var result = EditJsonPatch.SetPlain("""{"Z":"1","Db":{"Port":"5432"},"A":"2"}""", Service("Db:Host"), "h");

        Assert.Equal("""{"Z":"1","Db":{"Port":"5432","Host":"h"},"A":"2"}""", Compact(result));
    }

    [Fact]
    public void SetPlain_ExistingLeaf_ReplacesInPlace()
    {
        var result = EditJsonPatch.SetPlain("""{"A":"1","B":"2","C":"3"}""", Service("B"), "x");

        Assert.Equal("""{"A":"1","B":"x","C":"3"}""", Compact(result));
    }

    [Fact]
    public void SetPlain_Global_CreatesGlobalSectionAtEnd()
    {
        var result = EditJsonPatch.SetPlain("""{"A":"1"}""", Global("Logging:Level"), "Info");

        Assert.Equal("""{"A":"1","@global":{"Logging":{"Level":"Info"}}}""", Compact(result));
    }

    [Fact]
    public void SetPlain_Global_IntoExistingGlobal()
    {
        var result = EditJsonPatch.SetPlain("""{"@global":{"A":"1"},"B":"2"}""", Global("C"), "3");

        Assert.Equal("""{"@global":{"A":"1","C":"3"},"B":"2"}""", Compact(result));
    }

    [Fact]
    public void SetPlain_ReplacesSecretReference()
    {
        var result = EditJsonPatch.SetPlain("""{"Db":{"Password":{"@kv":"orders--Db--Password"}}}""", Service("Db:Password"), "plain");

        Assert.Equal("""{"Db":{"Password":"plain"}}""", Compact(result));
    }

    [Fact]
    public void SetPlain_PathThroughLeaf_ThrowsNamingKey_WithoutValue()
    {
        var ex = Assert.Throws<ArgumentException>(() => EditJsonPatch.SetPlain("""{"Db":"x"}""", Service("Db:Host"), Sentinel));

        Assert.Contains("Db:Host", ex.Message);
        Assert.DoesNotContain(Sentinel, ex.Message);
    }

    [Fact]
    public void SetPlain_PathThroughSecretReference_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            EditJsonPatch.SetPlain("""{"Db":{"@kv":"db"}}""", Service("Db:Host"), Sentinel));

        Assert.Contains("Db:Host", ex.Message);
        Assert.DoesNotContain(Sentinel, ex.Message);
    }

    [Fact]
    public void SetPlain_LeafWhereObjectIs_ThrowsNamingKey_WithoutValue()
    {
        var ex = Assert.Throws<ArgumentException>(() => EditJsonPatch.SetPlain("""{"Db":{"Host":"h"}}""", Service("Db"), Sentinel));

        Assert.Contains("Db", ex.Message);
        Assert.DoesNotContain(Sentinel, ex.Message);
    }

    [Fact]
    public void SetPlain_InvalidJson_ThrowsJsonException_WithoutValue()
    {
        var ex = Assert.ThrowsAny<JsonException>(() => EditJsonPatch.SetPlain($$"""{"A": "{{Sentinel}}" """, Service("B"), "x"));

        Assert.DoesNotContain(Sentinel, ex.ToString());
    }

    [Fact]
    public void SetPlain_NonObjectRoot_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => EditJsonPatch.SetPlain("[]", Service("A"), "x"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A::B")]
    [InlineData("A:@kv")]
    public void SetPlain_InvalidKey_ThrowsArgumentException(string key)
    {
        Assert.ThrowsAny<ArgumentException>(() => EditJsonPatch.SetPlain("{}", Service(key), "x"));
    }

    [Fact]
    public void SetPlain_ServiceKeyNamedGlobalSection_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => EditJsonPatch.SetPlain("{}", Service("@global:A"), "x"));
    }

    [Fact]
    public void SetPlain_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => EditJsonPatch.SetPlain(null!, Service("A"), "x"));
        Assert.Throws<ArgumentNullException>(() => EditJsonPatch.SetPlain("{}", null!, "x"));
        Assert.Throws<ArgumentNullException>(() => EditJsonPatch.SetPlain("{}", Service("A"), null!));
    }

    // SetSecretReference / SetSecretValue

    [Fact]
    public void SetSecretReference_ReplacesPlain()
    {
        var result = EditJsonPatch.SetSecretReference("""{"Db":{"Password":"old"}}""", Service("Db:Password"), "orders--Db--Password");

        Assert.Equal("""{"Db":{"Password":{"@kv":"orders--Db--Password"}}}""", Compact(result));
    }

    [Fact]
    public void SetSecretReference_ReplacesSecretValue()
    {
        var result = EditJsonPatch.SetSecretReference("""{"P":{"@kv":"old","value":"v"}}""", Service("P"), "new");

        Assert.Equal("""{"P":{"@kv":"new"}}""", Compact(result));
    }

    [Fact]
    public void SetSecretReference_BlankName_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => EditJsonPatch.SetSecretReference("{}", Service("P"), " "));
    }

    [Fact]
    public void SetSecretValue_WritesNameAndValue()
    {
        var result = EditJsonPatch.SetSecretValue("""{"P":"plain"}""", Global("P"), "global--P", new SecretString(Sentinel));

        Assert.Equal("{\"P\":\"plain\",\"@global\":{\"P\":{\"@kv\":\"global--P\",\"value\":\"" + Sentinel + "\"}}}", Compact(result));
    }

    [Fact]
    public void SetSecretValue_Clash_MessageWithoutValue()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            EditJsonPatch.SetSecretValue("""{"P":{"Q":"1"}}""", Service("P"), "p", new SecretString(Sentinel)));

        Assert.Contains("P", ex.Message);
        Assert.DoesNotContain(Sentinel, ex.Message);
    }

    // Remove

    [Fact]
    public void Remove_Missing_IsNoOp()
    {
        var result = EditJsonPatch.Remove("""{"A":"1"}""", Service("B:C"));

        Assert.Equal("""{"A":"1"}""", Compact(result));
    }

    [Fact]
    public void Remove_LastChild_RemovesEmptyParents()
    {
        var result = EditJsonPatch.Remove("""{"A":"1","B":{"C":{"D":"x"}}}""", Service("B:C:D"));

        Assert.Equal("""{"A":"1"}""", Compact(result));
    }

    [Fact]
    public void Remove_KeepsNonEmptyParent()
    {
        var result = EditJsonPatch.Remove("""{"B":{"C":"1","D":"2"}}""", Service("B:C"));

        Assert.Equal("""{"B":{"D":"2"}}""", Compact(result));
    }

    [Fact]
    public void Remove_LastGlobal_RemovesGlobalSection()
    {
        var result = EditJsonPatch.Remove("""{"A":"1","@global":{"L":{"M":"x"}}}""", Global("L:M"));

        Assert.Equal("""{"A":"1"}""", Compact(result));
    }

    [Fact]
    public void Remove_SecretReference()
    {
        var result = EditJsonPatch.Remove("""{"P":{"@kv":"p"},"Q":"1"}""", Service("P"));

        Assert.Equal("""{"Q":"1"}""", Compact(result));
    }

    [Fact]
    public void Remove_ServiceKey_DoesNotTouchGlobal()
    {
        var result = EditJsonPatch.Remove("""{"@global":{"A":"g"}}""", Service("A"));

        Assert.Equal("""{"@global":{"A":"g"}}""", Compact(result));
    }

    [Fact]
    public void Remove_ObjectWhereLeafExpected_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => EditJsonPatch.Remove("""{"A":{"B":"1"}}""", Service("A")));

        Assert.Contains("'A'", ex.Message);
    }

    // CopyKey / Get

    [Fact]
    public void CopyKey_SourceHasKey_SetsItInTarget()
    {
        var result = EditJsonPatch.CopyKey("""{"Db":{"Password":{"@kv":"db"}}}""", """{"A":"1","Db":{"Password":"old"}}""", Service("Db:Password"));

        Assert.Equal("""{"A":"1","Db":{"Password":{"@kv":"db"}}}""", Compact(result));
    }

    [Fact]
    public void CopyKey_SourceHasNoKey_RemovesItFromTarget()
    {
        var result = EditJsonPatch.CopyKey("""{"A":"1"}""", """{"A":"1","@global":{"G":"x"}}""", Global("G"));

        Assert.Equal("""{"A":"1"}""", Compact(result));
    }

    [Fact]
    public void Get_ReturnsDeepClone_OrNull()
    {
        const string json = """{"Db":{"Password":{"@kv":"db"}},"@global":{"A":"g"}}""";

        var node = EditJsonPatch.Get(json, Service("Db:Password"));

        AssertJson("""{"@kv":"db"}""", node!.ToJsonString());
        Assert.Null(node.Parent);
        Assert.Equal("g", EditJsonPatch.Get(json, Global("A"))!.GetValue<string>());
        Assert.Null(EditJsonPatch.Get(json, Service("A")));
        Assert.Null(EditJsonPatch.Get(json, Service("Db:Host")));
    }

    // ChangedKeys

    [Fact]
    public void ChangedKeys_AddedRemovedChanged_OrderedGlobalFirstThenKey()
    {
        const string before = """{"B":"1","A":"1","Keep":"k","Gone":"x","@global":{"G":"1"}}""";
        const string after = """{"A":"2","B":"1","Keep":"k","New":{"X":"1"},"@global":{"G":"2","H":"1"}}""";

        var changed = EditJsonPatch.ChangedKeys(before, after);

        Assert.Equal([Global("G"), Global("H"), Service("A"), Service("Gone"), Service("New:X")], changed);
    }

    [Fact]
    public void ChangedKeys_SecretReference_ComparesAsJson()
    {
        const string before = """{"P":{"@kv":"p"},"Q":{"@kv":"q"},"R":{"@kv":"r"}}""";
        const string after = """{"P":{"@kv":"p"},"Q":{"@kv":"q2"},"R":{"@kv":"r","value":"v"}}""";

        Assert.Equal([Service("Q"), Service("R")], EditJsonPatch.ChangedKeys(before, after));
    }

    [Fact]
    public void ChangedKeys_SameKeyDifferentScope_AreDistinct()
    {
        Assert.Equal([Global("A"), Service("A")], EditJsonPatch.ChangedKeys("""{"A":"1"}""", """{"@global":{"A":"1"}}"""));
    }

    [Fact]
    public void ChangedKeys_Identical_Empty()
    {
        Assert.Empty(EditJsonPatch.ChangedKeys("""{"A":{"B":"1"}}""", """{ "A": { "B": "1" } }"""));
    }

    // Round trip through JsonProjection

    [Fact]
    public void FromEditJson_OfPatchedExport_GivesExpectedState()
    {
        var fingerprinter = new HmacSecretFingerprinter(new byte[32]);
        var known = new StateValue.Secret(SecretUri, "fp");
        var export = JsonProjection.ToEditJson(
        [
            new("Db:Host", ConfigScope.Service, new ConfigValue.Plain("old")),
            new("Db:Password", ConfigScope.Service, new ConfigValue.KeyVaultRef(SecretUri)),
            new("Logging:Level", ConfigScope.Global, new ConfigValue.Plain("Info")),
            new("Remove:Me", ConfigScope.Service, new ConfigValue.Plain("x")),
        ]).ToJsonString();

        var json = EditJsonPatch.SetPlain(export, Service("Db:Host"), "new");
        json = EditJsonPatch.Remove(json, Service("Remove:Me"));
        json = EditJsonPatch.Remove(json, Global("Logging:Level"));
        json = EditJsonPatch.SetSecretValue(json, Service("Api:Key"), "orders--Api--Key", new SecretString(Sentinel));
        var parsed = JsonProjection.FromEditJson(json, fingerprinter, new Dictionary<string, StateValue.Secret> { ["orders--Db--Password"] = known });

        Assert.Equal(["Api:Key", "Db:Host", "Db:Password"], parsed.State.Items.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new StateValue.Plain("new"), parsed.State.Items["Db:Host"].Value);
        Assert.Equal(known, parsed.State.Items["Db:Password"].Value);
        Assert.Equal(new StateValue.Secret(null, fingerprinter.Fingerprint(new SecretString(Sentinel))), parsed.State.Items["Api:Key"].Value);
        var secret = Assert.Single(parsed.NewSecrets);
        Assert.Equal(("Api:Key", ConfigScope.Service, "orders--Api--Key", Sentinel), (secret.Key, secret.Scope, secret.Name, secret.Value.Reveal()));
        Assert.DoesNotContain("@global", json);
    }

    private static string Compact(string json) => JsonNode.Parse(json)!.ToJsonString();
}
