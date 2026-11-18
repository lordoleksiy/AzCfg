using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;

namespace AzCfg.Ui.Tests.Application.Environments;

public sealed class EnvironmentRulesTests
{
    private static ConfigEnvironment Env(string name, bool isProtected = false) => new(name, new Uri($"https://{name}.azconfig.io"), null, isProtected);

    private static ProjectBinding Binding(string? active) =>
        new("C:/src/Crm", "crm", Target(), Target(), new Dictionary<string, Uri>(), active);

    private static LocalTargetBinding Target() => new(LocalTargetKind.UserSecrets, "C:/src/Crm/Crm.csproj", LocalFileFormat.NestedJson);

    [Fact]
    public void IsActive_NameDiffersByCase_True() =>
        Assert.True(EnvironmentRules.IsActive(Binding("STAGE"), Env("stage")));

    [Fact]
    public void IsActive_NoActiveEnvironment_False() =>
        Assert.False(EnvironmentRules.IsActive(Binding(null), Env("dev")));

    [Fact]
    public void SelectDefault_ActiveListed_ReturnsActive() =>
        Assert.Equal("stage", EnvironmentRules.SelectDefault([Env("dev"), Env("stage")], Binding("Stage"))!.Name);

    [Fact]
    public void SelectDefault_ActiveDeleted_FallsBackToDev() =>
        Assert.Equal("DEV", EnvironmentRules.SelectDefault([Env("qa"), Env("DEV")], Binding("gone"))!.Name);

    [Fact]
    public void SelectDefault_NoDev_ReturnsFirst() =>
        Assert.Equal("qa", EnvironmentRules.SelectDefault([Env("qa"), Env("prod")], null)!.Name);

    [Fact]
    public void SelectDefault_Empty_ReturnsNull() =>
        Assert.Null(EnvironmentRules.SelectDefault([], Binding("dev")));

    [Theory]
    [InlineData("dev", false, EnvironmentKind.Development)]
    [InlineData("Development", false, EnvironmentKind.Development)]
    [InlineData("stage", false, EnvironmentKind.Staging)]
    [InlineData("STG", false, EnvironmentKind.Staging)]
    [InlineData("staging", false, EnvironmentKind.Staging)]
    [InlineData("test", false, EnvironmentKind.Staging)]
    [InlineData("qa", false, EnvironmentKind.Staging)]
    [InlineData("uat", false, EnvironmentKind.Staging)]
    [InlineData("prod", true, EnvironmentKind.Production)]
    [InlineData("dev", true, EnvironmentKind.Production)]
    [InlineData("sandbox", false, EnvironmentKind.Custom)]
    public void KindOf_Rule(string name, bool isProtected, EnvironmentKind expected) =>
        Assert.Equal(expected, EnvironmentRules.KindOf(Env(name, isProtected)));
}
