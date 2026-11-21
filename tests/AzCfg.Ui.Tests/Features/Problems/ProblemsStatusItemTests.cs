using AzCfg.Core.Health;
using AzCfg.Ui.Application.Health;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Features.Problems;
using AzCfg.Ui.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Features.Problems;

public sealed class ProblemsStatusItemTests : ProblemsContext
{
    [Fact]
    public void Render_CountsMatchTheReport()
    {
        Problems.Rows.AddRange([
            Row(HealthSeverity.Error, HealthCheckKind.AzureSignIn, "Not signed in.", fix: "az login"),
            Row(HealthSeverity.Error, HealthCheckKind.EnvironmentAccess, "prod unreachable.", "prod"),
            Row(HealthSeverity.Warning, HealthCheckKind.SecretNotVerified, "Not verified."),
        ]);

        var cut = Render<ProblemsStatusItem>();

        cut.WaitForAssertion(() => Assert.Equal("2", cut.Find(".az-problems-status-errors").TextContent));
        Assert.Equal("1", cut.Find(".az-problems-status-warnings").TextContent);
        var state = Services.GetRequiredService<ProblemsState>();
        Assert.Equal((state.Report!.Errors, state.Report.Warnings), (2, 1));
    }

    [Fact]
    public void Render_NoProject_RendersNothing()
    {
        Workspace.SelectProject(null);

        Assert.Empty(Render<ProblemsStatusItem>().Markup.Trim());
    }

    [Fact]
    public void Click_ShowsTheHiddenSidebar()
    {
        var layout = Services.GetRequiredService<LayoutState>();
        layout.ToggleSidebar();
        var cut = Render<ProblemsStatusItem>();

        cut.WaitForAssertion(() => cut.Find("button.az-problems-status").Click());

        Assert.True(layout.SidebarVisible);
    }

    [Fact]
    public void Service_IsOneReportShared_BySectionAndStatusItem()
    {
        Render<ProblemsSection>();
        Render<ProblemsStatusItem>();

        Assert.Single(Problems.Calls);
    }

    [Fact]
    public async Task RefreshCommand_RunsTheCheckAgain()
    {
        Render<ProblemsStatusItem>();
        var command = Services.GetRequiredService<IEnumerable<ICommandContributor>>().OfType<ProblemsCommands>().Single().Commands.Single();

        await command.Execute();

        Assert.Equal(ProblemsCommands.Refresh, command.Id);
        Assert.Equal(2, Problems.Calls.Count);
    }
}
