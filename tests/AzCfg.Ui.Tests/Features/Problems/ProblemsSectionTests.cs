using AzCfg.Core.Health;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Problems;
using AzCfg.Ui.Tests.Features.Sync;
using Bunit;

namespace AzCfg.Ui.Tests.Features.Problems;

public sealed class ProblemsSectionTests : ProblemsContext
{
    [Fact]
    public void Render_ShowsCountsGroupedBySeverity_AndRunsTheCheckAsRead()
    {
        Problems.Rows.AddRange([
            Row(HealthSeverity.Warning, HealthCheckKind.KeyMissingInEnvironments, "Key 'A' is missing in prod.", "prod", "A", "azcfg promote dev prod"),
            Row(HealthSeverity.Error, HealthCheckKind.DeadSecretReference, "Secret of 'B' does not exist.", "dev", "B"),
            Row(HealthSeverity.Warning, HealthCheckKind.ValuesTargetNotIgnored, "The values target is not git-ignored.", fix: "azcfg config set gitignoreLocalFiles true"),
        ]);

        var cut = Render<ProblemsSection>();

        cut.WaitForAssertion(() => Assert.Contains("1 errors", cut.Markup));
        Assert.Contains("2 warnings", cut.Markup);
        Assert.Single(cut.FindAll("[data-severity=error] .az-problem"));
        Assert.Equal(2, cut.FindAll("[data-severity=warning] .az-problem").Count);
        Assert.Contains(("Check project health", OperationKind.Read), Runner.Runs);
        Assert.Equal(["C:/src/Orders"], Problems.Calls);
    }

    [Fact]
    public void Render_NoFindings_ShowsEmptyState()
    {
        var cut = Render<ProblemsSection>();

        cut.WaitForAssertion(() => Assert.Contains("No problems found", cut.Markup));
        Assert.Contains("0 errors", cut.Markup);
    }

    [Fact]
    public void Render_NoProject_AsksForOne()
    {
        Workspace.SelectProject(null);

        var cut = Render<ProblemsSection>();

        Assert.Contains("Select a bound project", cut.Markup);
        Assert.Empty(Problems.Calls);
    }

    [Fact]
    public void Click_OpenInBrowse_SelectsTheKeyInBrowse()
    {
        Problems.Rows.Add(Row(HealthSeverity.Error, HealthCheckKind.DeadSecretReference, "Secret of 'Api:Key' does not exist.", "dev", "Api:Key", "azcfg edit --env dev"));
        var cut = Render<ProblemsSection>();

        Click(cut, "Open in Browse");

        var session = Sessions.GetOrCreate(SyncFixtures.ProjectId);
        Assert.Equal("Api:Key", session.SelectedKey);
        Assert.Equal("dev", session.EnvironmentName);
        Assert.Equal(ProjectMode.Browse, session.Mode);
        Assert.Contains(Tabs.Tabs, t => t.Descriptor.ProjectId == SyncFixtures.ProjectId);
    }

    [Fact]
    public void Click_OpenInBrowse_SeveralEnvironments_KeepsTheActiveOne()
    {
        Problems.Rows.Add(Row(HealthSeverity.Warning, HealthCheckKind.KeyMissingInEnvironments, "Key 'A' is missing in stage, prod.", "stage, prod", "A", "azcfg promote dev stage"));
        var cut = Render<ProblemsSection>();

        Click(cut, "Open in Browse");

        var session = Sessions.GetOrCreate(SyncFixtures.ProjectId);
        Assert.Equal("A", session.SelectedKey);
        Assert.Null(session.EnvironmentName);
    }

    [Theory]
    [InlineData(HealthCheckKind.ValuesTargetNotIgnored)]
    [InlineData(HealthCheckKind.BindingMismatch)]
    public void Click_OpenSettings_ForTargetAndBindingFindings(HealthCheckKind kind)
    {
        Problems.Rows.Add(Row(HealthSeverity.Warning, kind, "Binding problem.", fix: "azcfg config set project.service orders"));
        var cut = Render<ProblemsSection>();

        Click(cut, "Open settings");

        Assert.Contains(Tabs.Tabs, t => t.Descriptor.Kind == TabKind.Settings);
    }

    [Fact]
    public void Click_CopyFix_WritesTheCommandToTheClipboard()
    {
        Problems.Rows.Add(Row(HealthSeverity.Error, HealthCheckKind.AzureSignIn, "Not signed in to Azure.", fix: "az login"));
        var cut = Render<ProblemsSection>();

        Click(cut, "Copy fix");

        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, i => i.Identifier == "navigator.clipboard.writeText" && i.Arguments[0] as string == "az login"));
    }

    [Fact]
    public void Finding_WithoutKeyOrFix_HasNoActions()
    {
        Problems.Rows.Add(Row(HealthSeverity.Error, HealthCheckKind.EnvironmentAccess, "Environment 'prod' is not reachable.", "prod"));

        var cut = Render<ProblemsSection>();

        cut.WaitForAssertion(() => Assert.Contains("not reachable", cut.Markup));
        Assert.Empty(cut.FindAll(".az-problem fluent-button"));
    }

    [Fact]
    public void Refresh_RunsTheCheckAgain()
    {
        var cut = Render<ProblemsSection>();
        cut.WaitForAssertion(() => Assert.Contains("No problems found", cut.Markup));
        Problems.Rows.Add(Row(HealthSeverity.Warning, HealthCheckKind.SecretNotVerified, "Secret of 'S' could not be verified."));

        cut.Find("fluent-button[title='Refresh problems']").Click();

        cut.WaitForAssertion(() => Assert.Contains("1 warnings", cut.Markup));
        Assert.Equal(2, Problems.Calls.Count);
    }

    [Fact]
    public void ViewReplaced_AfterAWrite_ChecksAgain()
    {
        var session = Sessions.GetOrCreate(SyncFixtures.ProjectId);
        session.SetView(View());
        var cut = Render<ProblemsSection>();
        cut.WaitForAssertion(() => Assert.Single(Problems.Calls));

        cut.InvokeAsync(() =>
        {
            session.Invalidate();
            session.SetView(View());
        });

        cut.WaitForAssertion(() => Assert.Equal(2, Problems.Calls.Count));
    }

    [Fact]
    public void Render_SentinelSecretValue_NeverInMarkup()
    {
        Problems.Rows.Add(Row(HealthSeverity.Error, HealthCheckKind.DeadSecretReference, "Secret of 'Api:Key' does not exist.", "dev", "Api:Key", "azcfg edit --env dev"));

        var cut = Render<ProblemsSection>();
        var status = Render<ProblemsStatusItem>();

        cut.WaitForAssertion(() => Assert.Contains("Api:Key", cut.Markup));
        Assert.DoesNotContain(SyncFixtures.Sentinel, cut.Markup);
        Assert.DoesNotContain(SyncFixtures.Sentinel, status.Markup);
    }

    private static ConfigurationView View() => new(SyncFixtures.Binding(), SyncFixtures.Dev, false, [], [], DateTimeOffset.UnixEpoch);
}
