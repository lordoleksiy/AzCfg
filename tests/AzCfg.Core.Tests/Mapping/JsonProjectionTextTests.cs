using AzCfg.Core.Mapping;
using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Mapping;

/// <summary><see cref="JsonProjection.ToNestedJsonText"/> and <see cref="JsonProjection.ReadGlobalMarkers"/> (ADR 0009, U3/U4).</summary>
public class JsonProjectionTextTests
{
    private static string Lines(string text) => text.ReplaceLineEndings(Environment.NewLine);

    private static LocalValue Global(string key, string value) => new(key, value, ConfigScope.Global);

    [Fact]
    public void ToNestedJsonText_Empty_IsEmptyObject() =>
        Assert.Equal("{}", JsonProjection.ToNestedJsonText([]));

    [Fact]
    public void ToNestedJsonText_NoGlobalValues_EqualsIndentedToNestedJson()
    {
        LocalValue[] values =
        [
            new("Logging:LogLevel:Default", "Information"),
            new("Api:Url", "https://x?a=1&b=<2>"),
            new("Quote", "say \"hi\"\n\ttab \\ é 'q' + Ω"),
            new("Ключ:Назва", "значення"),
            new("Empty", ""),
        ];

        Assert.Equal(
            JsonProjection.ToNestedJson(values).ToJsonString(new() { WriteIndented = true }),
            JsonProjection.ToNestedJsonText(values));
    }

    [Fact]
    public void ToNestedJsonText_GlobalLeaves_GetMarkerAfterComma()
    {
        var text = JsonProjection.ToNestedJsonText(
        [
            Global("Logging:LogLevel", "Information"),
            Global("Api:Url", "https://x"),
            new("Api:Timeout", "30"),
        ]);

        Assert.Equal(Lines("""
            {
              "Api": {
                "Timeout": "30",
                "Url": "https://x" //@global
              },
              "Logging": {
                "LogLevel": "Information" //@global
              }
            }
            """), text);
    }

    [Fact]
    public void ToNestedJsonText_GlobalLeafFollowedBySibling_HasCommaBeforeMarker()
    {
        var text = JsonProjection.ToNestedJsonText([Global("A", "1"), new("B", "2")]);

        Assert.Equal(Lines("""
            {
              "A": "1", //@global
              "B": "2"
            }
            """), text);
    }

    [Fact]
    public void ToNestedJsonText_IsValidJsonWithComments_AndKeepsValues()
    {
        LocalValue[] values = [Global("A:B", "x\"y"), new("A:C", "z"), Global("D", "é")];

        var text = JsonProjection.ToNestedJsonText(values);

        var options = new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip };
        var withoutComments = System.Text.Json.Nodes.JsonNode.Parse(text, documentOptions: options)!.ToJsonString();
        Assert.Equal(JsonProjection.ToNestedJson(values).ToJsonString(), withoutComments);
    }

    [Fact]
    public void ToNestedJsonText_InvalidValues_ThrowLikeToNestedJson()
    {
        Assert.Throws<ArgumentNullException>(() => JsonProjection.ToNestedJsonText(null!));
        Assert.Throws<ArgumentException>(() => JsonProjection.ToNestedJsonText([new("A", "1"), Global("A:B", "2")]));
    }

    [Fact]
    public void ReadGlobalMarkers_RoundTripsToNestedJsonText()
    {
        LocalValue[] values = [Global("Logging:LogLevel:Default", "Information"), new("Api:Url", "u"), Global("Api:Key", "k"), Global("Z", "z"), new("Y", "y")];

        var markers = JsonProjection.ReadGlobalMarkers(JsonProjection.ToNestedJsonText(values));

        Assert.Equal(["Api:Key", "Logging:LogLevel:Default", "Z"], markers.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ReadGlobalMarkers_IsCaseInsensitive() =>
        Assert.Contains("api:url", JsonProjection.ReadGlobalMarkers("{ \"Api\": { \"Url\": \"x\" //@global\n } }"));

    [Theory]
    [InlineData("{ \"A\": \"1\", //@global\n \"B\": \"2\" }")]
    [InlineData("{ \"A\": \"1\", // @global\r\n \"B\": \"2\" }")]
    [InlineData("{ \"A\": \"1\" /*@global*/, \"B\": \"2\" }")]
    [InlineData("{ \"A\": \"1\", /* @global */ \"B\": \"2\" }")]
    [InlineData("{ \"B\": \"2\", \"A\": 1, //@global\n }")]
    [InlineData("{ \"B\": \"2\", \"A\": true /*@global*/ }")]
    public void ReadGlobalMarkers_AcceptsCommentVariants(string json) =>
        Assert.Equal(["A"], JsonProjection.ReadGlobalMarkers(json));

    [Theory]
    [InlineData("{ \"A\": \"1\", // note\n \"B\": \"2\" }")]
    [InlineData("{ \"A\": \"1\",\n // @global\n \"B\": \"2\" }")]
    [InlineData("{ \"A\": { \"B\": \"1\" } //@global\n }")]
    [InlineData("{ /*@global*/ \"A\": \"1\" }")]
    [InlineData("{ \"A\": \"1\" }")]
    public void ReadGlobalMarkers_OtherCommentsOrPlacement_AreNotMarkers(string json) =>
        Assert.Empty(JsonProjection.ReadGlobalMarkers(json));

    [Fact]
    public void ReadGlobalMarkers_Arrays_UseIndexSegments()
    {
        var markers = JsonProjection.ReadGlobalMarkers("""
            {
              "A": [
                "x",
                "y", //@global
                { "B": "z" //@global
                }
              ],
              "C": "w" //@global
            }
            """);

        Assert.Equal(["A:1", "A:2:B", "C"], markers.Order(StringComparer.Ordinal));
    }
}
