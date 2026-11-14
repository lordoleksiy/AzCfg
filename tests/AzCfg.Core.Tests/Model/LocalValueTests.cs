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
}
