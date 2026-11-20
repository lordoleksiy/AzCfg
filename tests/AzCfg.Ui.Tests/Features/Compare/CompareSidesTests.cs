using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Features.Compare;

namespace AzCfg.Ui.Tests.Features.Compare;

public sealed class CompareSidesTests
{
    private static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly ConfigEnvironment Stage = new("stage", new Uri("https://stage.azconfig.io"), null, false);
    private static readonly IReadOnlyList<ConfigEnvironment> Envs = [Dev, Stage];

    private static ProjectBinding Binding(string? active)
    {
        var target = new LocalTargetBinding(LocalTargetKind.UserSecrets, "C:/src/Crm/Crm.csproj", LocalFileFormat.NestedJson);
        return new ProjectBinding("C:/src/Crm", "crm", target, target, new Dictionary<string, Uri>(), active);
    }

    [Fact]
    public void Default_SelectedIsActive_LocalAgainstSelected() =>
        Assert.Equal((new CompareSide.Local(), new CompareSide.Environment(Dev)), CompareSides.Default(Binding("dev"), Envs, "DEV"));

    [Fact]
    public void Default_SelectedIsNotActive_ActiveAgainstSelected() =>
        Assert.Equal((new CompareSide.Environment(Dev), new CompareSide.Environment(Stage)), CompareSides.Default(Binding("dev"), Envs, "stage"));

    [Fact]
    public void Default_NoActiveEnvironment_LocalAgainstSelected() =>
        Assert.Equal((new CompareSide.Local(), new CompareSide.Environment(Stage)), CompareSides.Default(Binding(null), Envs, "stage"));

    [Fact]
    public void Default_NoSelection_UsesTheDefaultEnvironment() =>
        Assert.Equal((new CompareSide.Local(), new CompareSide.Environment(Stage)), CompareSides.Default(Binding("stage"), Envs, null));

    [Fact]
    public void Default_NoEnvironments_Null() => Assert.Null(CompareSides.Default(Binding("dev"), [], "dev"));

    [Fact]
    public void Name_LocalAndEnvironment() =>
        Assert.Equal(("Local", "stage"), (CompareSides.Name(new CompareSide.Local()), CompareSides.Name(new CompareSide.Environment(Stage))));

    [Theory]
    [InlineData(CompareResult.Same, false, false, "Same value")]
    [InlineData(CompareResult.Different, false, false, "Different values")]
    [InlineData(CompareResult.Same, true, false, "Same secret value")]
    [InlineData(CompareResult.Different, true, false, "Different secret value")]
    [InlineData(CompareResult.Same, true, true, "Same secret reference · values not compared")]
    [InlineData(CompareResult.Different, true, true, "Different secret reference · values not compared")]
    [InlineData(CompareResult.OnlyLeft, false, true, "Only in dev")]
    [InlineData(CompareResult.OnlyRight, false, true, "Only in stage")]
    public void Describe_ResultsAndSecrets(CompareResult result, bool secret, bool remoteOnly, string expected)
    {
        var cell = new CompareCell(secret ? null : "v", secret, secret ? "crm--Db" : null, ConfigScope.Service, null);
        var row = new CompareRow("Db", result, result == CompareResult.OnlyRight ? null : cell, result == CompareResult.OnlyLeft ? null : cell);
        CompareSide left = remoteOnly ? new CompareSide.Environment(Dev) : new CompareSide.Local();
        Assert.Equal(expected, CompareSides.Describe(row, left, new CompareSide.Environment(Stage)));
    }
}
