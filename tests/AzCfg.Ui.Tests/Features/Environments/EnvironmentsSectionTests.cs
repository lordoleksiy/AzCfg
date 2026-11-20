using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Environments;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Workspace;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Workspace.ShellFixtures;

namespace AzCfg.Ui.Tests.Features.Environments;

public sealed class EnvironmentsSectionTests : ShellContext
{
    private static readonly string Orders = IdOf("Orders.Api");

    private void SeedProject(string? active = "dev")
    {
        SeedTree(Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api", active: active)))]));
        SeedEnvironments();
    }

    private static string[] Names(IRenderedComponent<EnvironmentsSection> cut) => [.. cut.FindAll(".az-env-name").Select(e => e.TextContent)];

    [Fact]
    public void Render_NotLoaded_LoadsTheProfileThroughARead()
    {
        var cut = Render<EnvironmentsSection>();
        Assert.Equal(["dev", "prod"], Names(cut));
        Assert.Equal(1, EnvService.Loads);
        Assert.Contains(("Load environments", OperationKind.Read), Runner.Runs);
    }

    [Fact]
    public void Render_Loaded_DoesNotLoadAgain()
    {
        SeedEnvironments();
        Render<EnvironmentsSection>();
        Assert.Equal(0, EnvService.Loads);
    }

    [Fact]
    public void Render_FirstRun_ShowsSetUpAndExecutesTheCommand()
    {
        EnvService.Snapshot = new EnvironmentsSnapshot(null, []);
        var ran = false;
        Commands.Register(new UiCommand(CommandIds.EnvSetup, "Set up", "Env", null, () => { ran = true; return Task.CompletedTask; }));
        var cut = Render<EnvironmentsSection>();
        Assert.Contains("Set up environments", cut.Markup);
        Click(cut, "Set up…");
        Assert.True(ran);
    }

    [Fact]
    public void Render_LoadFails_ShowsTheErrorWithRetry()
    {
        EnvService.LoadFailure = new HttpRequestException("offline");
        var cut = Render<EnvironmentsSection>();
        Assert.Contains("Retry", cut.FindAll("fluent-button").Select(b => b.TextContent.Trim()));
    }

    [Fact]
    public void Render_ProtectedEnvironment_IsMarkedWithLockAndFrame()
    {
        SeedEnvironments();
        var cut = Render<EnvironmentsSection>();
        Assert.Contains("protected", cut.Find(".az-env-item[data-env='prod']").ClassList);
        Assert.DoesNotContain("protected", cut.Find(".az-env-item[data-env='dev']").ClassList);
        Assert.Contains("Protected", cut.Find(".az-env-item[data-env='prod']").InnerHtml);
        Assert.DoesNotContain("Protected", cut.Find(".az-env-item[data-env='dev']").InnerHtml);
    }

    [Fact]
    public void Render_Kinds_UseTheKindColours()
    {
        EnvService.Snapshot = new EnvironmentsSnapshot(Profile(Dev, Stage, Prod), [Dev, Stage, Prod]);
        var cut = Render<EnvironmentsSection>();
        Assert.Equal(["az-env-dot kind-development", "az-env-dot kind-staging", "az-env-dot kind-production"], cut.FindAll(".az-env-dot").Select(d => string.Join(' ', d.ClassList.Where(c => c != "b-"))).Select(c => string.Join(' ', c.Split(' ').Where(x => x.StartsWith("az-") || x.StartsWith("kind-")))));
    }

    [Fact]
    public void Render_LabelMode_ShowsTheLabelAsDetail()
    {
        var labelled = new ConfigEnvironment("dev", new Uri("https://shared.azconfig.io"), "Development", false);
        EnvService.Snapshot = new EnvironmentsSnapshot(new UserProfile([labelled], EnvironmentMode.LabelPerEnvironment), [labelled]);
        var cut = Render<EnvironmentsSection>();
        Assert.Equal("label Development", cut.Find(".az-env-detail").TextContent);
    }

    [Fact]
    public void Render_ChecksEachEnvironmentOnceAndShowsTheState()
    {
        SeedProject();
        Workspace.SelectProject(Orders);
        var cut = Render<EnvironmentsSection>();
        cut.WaitForAssertion(() => Assert.Equal(["connected", "connected"], cut.FindAll(".az-env-state").Select(s => s.TextContent)));
        Assert.Equal([("dev", (string?)"orders"), ("prod", "orders")], Status.EnvironmentChecks);
        Render<EnvironmentsSection>();
        Assert.Equal(2, Status.EnvironmentChecks.Count);
    }

    [Fact]
    public void Render_UnreachableEnvironment_ShowsItsState()
    {
        Status.NextEnvironmentState = ConnectionState.AuthRequired;
        SeedEnvironments();
        var cut = Render<EnvironmentsSection>();
        cut.WaitForAssertion(() => Assert.Equal(["sign in", "sign in"], cut.FindAll(".az-env-state").Select(s => s.TextContent)));
    }

    [Fact]
    public void Click_Environment_SelectsItInTheActiveProjectTabWithoutSideEffects()
    {
        SeedProject();
        Services.GetRequiredService<WorkbenchNavigator>().OpenProject(Orders);
        var cut = Render<EnvironmentsSection>();
        cut.Find(".az-env-item[data-env='prod'] .az-env-main").Click();
        Assert.Equal("prod", Sessions.GetOrCreate(Orders).EnvironmentName);
        Assert.Empty(Activation.Calls);
        cut.WaitForAssertion(() => Assert.Contains("selected", cut.Find(".az-env-item[data-env='prod']").ClassList));
    }

    [Fact]
    public void Click_Environment_NoProject_ToastsInsteadOfChangingAnything()
    {
        SeedEnvironments();
        var providers = Providers;
        var cut = Render<EnvironmentsSection>();
        cut.Find(".az-env-item[data-env='dev'] .az-env-main").Click();
        providers.WaitForAssertion(() => Assert.Contains("Open a project first", providers.Markup));
        Assert.Empty(Tabs.Tabs);
    }

    [Fact]
    public void MakeActive_OtherEnvironment_RunsTheActivationFlow()
    {
        SeedProject("dev");
        Workspace.SelectProject(Orders);
        var cut = Render<EnvironmentsSection>();
        Assert.True(ButtonTitled(cut, "Make dev active").HasAttribute("disabled"));
        ButtonTitled(cut, "Make prod active").Click();
        Assert.Equal([(Orders, "prod")], Activation.Calls);
    }

    [Fact]
    public void MakeActive_NoBoundProject_IsDisabled()
    {
        SeedEnvironments();
        var cut = Render<EnvironmentsSection>();
        Assert.True(ButtonTitled(cut, "Make dev active").HasAttribute("disabled"));
    }

    [Fact]
    public void CheckConnection_ProbesAgain()
    {
        SeedProject();
        Workspace.SelectProject(Orders);
        var cut = Render<EnvironmentsSection>();
        var before = Status.EnvironmentChecks.Count;
        ButtonTitled(cut, "Check connection to prod").Click();
        Assert.Equal(before + 1, Status.EnvironmentChecks.Count);
        Assert.Equal(("prod", "orders"), Status.EnvironmentChecks.Last());
    }

    [Fact]
    public void Manage_ExecutesTheManageCommand()
    {
        SeedEnvironments();
        var ran = false;
        Commands.Register(new UiCommand(CommandIds.EnvManage, "Manage", "Env", null, () => { ran = true; return Task.CompletedTask; }));
        Render<EnvironmentsSection>().Find(".az-envs-manage button").Click();
        Assert.True(ran);
    }

    [Fact]
    public void SavingEnvironments_RerendersTheRows()
    {
        SeedEnvironments();
        var cut = Render<EnvironmentsSection>();
        cut.InvokeAsync(() => Workspace.SetEnvironments(new EnvironmentsSnapshot(Profile(Dev), [Dev])));
        cut.WaitForAssertion(() => Assert.Equal(["dev"], Names(cut)));
    }
}
