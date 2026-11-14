using System.Text.Json.Nodes;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;

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
}
