using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public sealed class PushSourceRedactionTests
{
    private const string Sentinel = "AZCFG-TEST-SECRET-7f3a";
    private const string Json = """{"Db":{"Password":{"@kv":"Db--Password","value":""" + "\"" + Sentinel + "\"}}}";

    [Fact]
    public void EditedJson_ToString_RedactsJson()
    {
        var text = new PushSource.EditedJson(Json).ToString();

        Assert.DoesNotContain(Sentinel, text);
        Assert.DoesNotContain("Db--Password", text);
        Assert.Equal("EditedJson { Json = ***, BaseJson = null }", text);
    }

    [Fact]
    public void EditedJson_ToString_RedactsBaseJson()
    {
        var text = new PushSource.EditedJson(Json, Json).ToString();

        Assert.DoesNotContain(Sentinel, text);
        Assert.Equal("EditedJson { Json = ***, BaseJson = *** }", text);
    }

    [Fact]
    public void Promote_ToString_RedactsJsonAndKeepsFromEnvironment()
    {
        var text = new PushSource.Promote(Json, "stage").ToString();

        Assert.DoesNotContain(Sentinel, text);
        Assert.Equal("Promote { Json = ***, FromEnvironment = stage }", text);
    }

    [Fact]
    public void EditedJson_Equality_StillComparesValues()
    {
        Assert.Equal(new PushSource.EditedJson(Json, "{}"), new PushSource.EditedJson(Json, "{}"));
        Assert.NotEqual(new PushSource.EditedJson(Json), new PushSource.EditedJson(Json, "{}"));
    }
}
