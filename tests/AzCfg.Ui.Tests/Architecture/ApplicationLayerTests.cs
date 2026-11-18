using AzCfg.Ui.Application;

namespace AzCfg.Ui.Tests.Architecture;

public sealed class ApplicationLayerTests
{
    [Theory]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Microsoft.JSInterop")]
    [InlineData("Microsoft.FluentUI")]
    public void UiApplication_DoesNotReferenceUiFrameworks(string prefix) =>
        Assert.DoesNotContain(
            typeof(ApplicationServices).Assembly.GetReferencedAssemblies(),
            a => a.Name!.StartsWith(prefix, StringComparison.Ordinal));
}
