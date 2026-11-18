using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Model;

public class LocalValueTests
{
    [Fact]
    public void ToString_DoesNotContainValue_ButContainsKey()
    {
        var value = new LocalValue("ConnectionStrings:Crm", "super-secret-value");

        var text = value.ToString();

        Assert.DoesNotContain("super-secret-value", text);
        Assert.Contains("ConnectionStrings:Crm", text);
        Assert.Contains("***", text);
    }

    [Fact]
    public void Scope_DefaultsToService_AndIsPrinted()
    {
        Assert.Equal(ConfigScope.Service, new LocalValue("A", "1").Scope);
        Assert.Equal("LocalValue { Key = A, Value = ***, Scope = Global }", new LocalValue("A", "s", ConfigScope.Global).ToString());
    }
}
