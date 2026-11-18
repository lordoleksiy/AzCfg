using AzCfg.Ui.Components.Editors;

namespace AzCfg.Ui.Tests.Components.Editors;

public sealed class JsonKeyLocatorTests
{
    private const string Json = "{\n  \"Db\": {\n    \"Host\": \"x\",\n    \"Port\": 1\n  },\n  \"@global\": {\n    \"Shared\": { \"Key\": 2 }\n  }\n}";

    [Theory]
    [InlineData("Db", 2, 3)]
    [InlineData("Db:Host", 3, 5)]
    [InlineData("Db:Port", 4, 5)]
    [InlineData("Shared:Key", 7, 17)]
    public void Find_NestedKey_ReturnsPropertyPosition(string key, int line, int column) =>
        Assert.Equal(new JsonKeyLocator.Location(line, column), JsonKeyLocator.Find(Json, key));

    [Theory]
    [InlineData("Db:Missing")]
    [InlineData("Host")]
    [InlineData("")]
    public void Find_UnknownKey_ReturnsNull(string key) => Assert.Null(JsonKeyLocator.Find(Json, key));

    [Fact]
    public void Find_InvalidJson_ReturnsNull() => Assert.Null(JsonKeyLocator.Find("{ \"Db\": ", "Db:Host"));
}
