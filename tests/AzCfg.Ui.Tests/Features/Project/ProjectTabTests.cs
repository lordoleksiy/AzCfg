using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Features.Project;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Browse;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Browse.BrowseFixtures;

namespace AzCfg.Ui.Tests.Features.Project;

public sealed class ProjectTabTests : BrowseContext
{
    private static IReadOnlyList<ConfigRow> Rows() => [Plain("Db:Host", "h1", "h1"), Plain("Db:Port", "1", "2", DiffKind.LocalModified)];

    private static RemoteDraft Draft(string env = "dev") =>
        new(Binding(), env == "dev" ? Dev : Prod, "{}", "{}", new Dictionary<DraftKey, DraftSecret>());

    private IRenderedComponent<ProjectTab> Open(ProjectSession session)
    {
        var cut = RenderTab(session);
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(session.View);
            Assert.Empty(cut.FindAll(".az-browse-skeleton"));
        });
        return cut;
    }

    private void ClickDialog(string text) =>
        Providers.WaitForAssertion(() => Providers.FindAll("fluent-button").First(b => b.TextContent.Trim() == text).Click());

    [Fact]
    public void AddProjectFeature_RegistersTheProjectTab()
    {
        var registration = Assert.Single(new ServiceCollection().AddProjectFeature().BuildServiceProvider().GetServices<TabRegistration>());
        Assert.Equal((TabKind.Project, typeof(ProjectTab)), (registration.Kind, registration.Component));
    }

    [Fact]
    public void Render_ShowsProjectServiceEnvironmentPillsAndModeSwitch()
    {
        var cut = Open(Seed(Rows()));
        Assert.Equal("Orders", cut.Find(".az-project-name").TextContent);
        Assert.Contains("orders", cut.Find(".az-project-service").TextContent);
        var pills = cut.FindAll(".az-envpill-name").Select(p => p.TextContent.Trim()).ToList();
        Assert.Equal(["dev", "prod"], pills);
        Assert.NotEmpty(cut.Find(".az-envpill.selected").QuerySelectorAll(".az-envpill-active"));
        Assert.Equal(["Browse", "Compare", "Sync"], cut.FindAll(".az-project-mode").Select(b => b.TextContent.Trim()));
        Assert.Equal("true", cut.FindAll(".az-project-mode")[0].GetAttribute("aria-selected"));
    }

    [Fact]
    public void Render_ActiveEnvironment_ShowsTheSummaryFromStatusAsync_AndPendingCountOnSync()
    {
        Sync.Summary = new SyncSummary("dev", true, 2, 1, ["Db:Host"], [], [], 0);
        var cut = Open(Seed(Rows()));
        cut.WaitForAssertion(() => Assert.Contains("⇡ 2", cut.Find(".az-project-summary").TextContent));
        var summary = cut.Find(".az-project-summary").TextContent;
        Assert.Contains("⇣ 1", summary);
        Assert.Contains("! 1", summary);
        Assert.Equal("Sync (4)", cut.FindAll(".az-project-mode")[2].TextContent.Trim());
        Assert.Equal(1, Sync.StatusCalls);
    }

    [Fact]
    public void Render_NonActiveEnvironment_HasNoSummaryAndNeverCallsStatus()
    {
        var cut = Open(Seed(Rows(), active: "dev", env: "prod"));
        Assert.Empty(cut.FindAll(".az-project-summary"));
        Assert.Equal(0, Sync.StatusCalls);
    }

    [Fact]
    public void Render_NonActiveEnvironment_ShowsRemoteOnlyBarAndMakeActiveRunsTheFlow()
    {
        var cut = Open(Seed(Rows(), active: "dev", env: "prod"));
        Assert.Contains("Viewing prod — remote only. Local files reflect dev.", cut.Find("fluent-message-bar").TextContent);
        Button(cut, "Make prod active").Click();
        Assert.Equal([(ProjectId, "prod")], Activation.Calls);
    }

    [Fact]
    public void Render_ActiveEnvironment_HasNoRemoteOnlyBar()
    {
        var cut = Open(Seed(Rows()));
        Assert.Empty(cut.FindAll("fluent-message-bar"));
    }

    [Fact]
    public void ClickingAnEnvironmentPill_SwitchesTheSessionAndLoadsIt()
    {
        var session = Seed(Rows());
        var cut = Open(session);
        cut.FindAll(".az-envpill").First(p => p.TextContent.Contains("prod", StringComparison.Ordinal)).Click();
        Assert.Equal("prod", session.EnvironmentName);
        cut.WaitForAssertion(() => Assert.Equal(["dev", "prod"], Config.Loads));
    }

    [Fact]
    public void OverflowMenu_OffersBindingSettings_AndMakeActiveForAnInactiveEnvironment()
    {
        var cut = Open(Seed(Rows(), active: "dev", env: "prod"));
        cut.Find("fluent-button[title=More]").Click();
        var items = cut.FindAll(".az-project-menu [role=menuitem]").Select(i => i.TextContent.Trim()).ToList();
        Assert.Equal(["Binding settings…", "Make prod active"], items);
        cut.FindAll(".az-project-menu [role=menuitem]")[1].Click();
        Assert.Equal([(ProjectId, "prod")], Activation.Calls);
    }

    [Fact]
    public void ProtectedEnvironment_ShowsFrameAndReadOnlyBar_UnlockAfterConfirmationAndLock()
    {
        var cut = Open(Seed(Rows(), active: "prod", env: "prod"));
        Assert.NotNull(cut.Find(".az-project.protected"));
        Assert.Contains("PROTECTED · read-only", cut.Find(".az-project-protected").TextContent);

        Button(cut, "Unlock editing").Click();
        ClickDialog("Unlock");
        cut.WaitForAssertion(() => Assert.Contains("Editing unlocked", cut.Find(".az-project-protected").TextContent));
        Assert.True(Unlock.IsUnlocked("prod"));

        Button(cut, "Lock").Click();
        cut.WaitForAssertion(() => Assert.Contains("read-only", cut.Find(".az-project-protected").TextContent));
        Assert.False(Unlock.IsUnlocked("prod"));
    }

    [Fact]
    public void ProtectedEnvironment_UnlockCancelled_StaysLocked()
    {
        var cut = Open(Seed(Rows(), active: "prod", env: "prod"));
        Button(cut, "Unlock editing").Click();
        ClickDialog("Cancel");
        Assert.False(Unlock.IsUnlocked("prod"));
        Assert.Contains("read-only", cut.Find(".az-project-protected").TextContent);
    }

    [Fact]
    public void UnprotectedEnvironment_HasNoFrame()
    {
        var cut = Open(Seed(Rows()));
        Assert.Empty(cut.FindAll(".az-project.protected"));
        Assert.Empty(cut.FindAll(".az-project-protected"));
    }

    [Fact]
    public void DraftBar_ShowsPendingCount_AndMarksTheTabDirty()
    {
        var session = Seed(Rows());
        var cut = Open(session);
        Assert.Empty(cut.FindAll(".az-draftbar"));
        Assert.False(Tabs.Tabs.Single().IsDirty);

        session.SetDraft("dev", Draft(), 2);
        cut.WaitForAssertion(() => Assert.Contains("2 pending edits to dev", cut.Find(".az-draftbar").TextContent));
        Assert.True(Tabs.Tabs.Single().IsDirty);
    }

    [Fact]
    public void InlineEdit_InsideTheTab_KeepsHeaderGridAndDraftBarRendered()
    {
        var session = Seed(Rows());
        var cut = Open(session);
        ClickRow(cut, "Db:Host");
        Press(cut, "Db:Host", "Enter");
        var input = cut.Find("input[aria-label=\"Value of Db:Host\"]");
        input.Input("h2");
        input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-draftbar")));
        Assert.NotEmpty(cut.FindAll(".az-project-header"));
        Assert.NotEmpty(cut.FindAll("[role=treegrid] [data-path=\"Db:Host\"]"));
        Assert.Contains("1 pending edits to dev", cut.Find(".az-draftbar").TextContent);
        Assert.True(Tabs.Tabs.Single().IsDirty);
    }

    [Fact]
    public void InlineEdit_FocusesTheInputWhenTheEditStarts()
    {
        var cut = Open(Seed(Rows()));
        ClickRow(cut, "Db:Host");
        Press(cut, "Db:Host", "Enter");
        cut.WaitForAssertion(() => Assert.NotEmpty(JSInterop.Invocations["Blazor._internal.domWrapper.focus"]));
    }

    [Fact]
    public void ProjectCss_FillsTheTabHostInsteadOfUsingAPercentageHeight()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AzCfg.slnx")))
        {
            dir = dir.Parent;
        }

        var css = File.ReadAllText(Path.Combine(dir!.FullName, "src", "AzCfg.Ui", "Features", "Project", "ProjectTab.razor.css"));
        Assert.Contains("position: absolute", css);
        Assert.DoesNotContain("height: 100%", css);
    }

    [Fact]
    public void DraftBar_OnlyShowsEditsOfTheSelectedEnvironment()
    {
        var session = Seed(Rows());
        var cut = Open(session);
        session.SetDraft("prod", Draft("prod"), 1);
        cut.WaitForAssertion(() => Assert.True(Tabs.Tabs.Single().IsDirty));
        Assert.Empty(cut.FindAll(".az-draftbar"));
    }

    [Fact]
    public void DraftBar_Discard_AsksThenDropsTheDraftAndCleansTheTab()
    {
        var session = Seed(Rows());
        var cut = Open(session);
        session.SetDraft("dev", Draft(), 2);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-draftbar")));
        Button(cut, "Discard").Click();
        Providers.WaitForAssertion(() => Assert.Contains("Discard 2 pending edits to dev?", Providers.Markup));
        ClickDialog("Discard");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".az-draftbar")));
        Assert.Null(session.DraftFor("dev"));
        Assert.False(Tabs.Tabs.Single().IsDirty);
    }

    [Fact]
    public void DraftBar_Review_OpensSyncPendingEdits()
    {
        var session = Seed(Rows());
        var cut = Open(session);
        session.SetDraft("dev", Draft(), 1);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-draftbar")));
        Button(cut, "Review ▸").Click();
        Assert.Equal((ProjectMode.Sync, SyncView.Draft), (session.Mode, session.SyncView));
    }

    [Fact]
    public async Task ClosingADirtyTab_AsksToDiscardThePendingEdits()
    {
        var session = Seed(Rows());
        var cut = Open(session);
        session.SetDraft("dev", Draft(), 2);
        cut.WaitForAssertion(() => Assert.True(Tabs.Tabs.Single().IsDirty));
        var closing = Services.GetRequiredService<TabCloser>().CloseAsync(Tabs.Tabs.Single().Id);
        Providers.WaitForAssertion(() => Assert.Contains("Discard 2 pending edits?", Providers.Markup));
        ClickDialog("Cancel");
        Assert.False(await closing);
        Assert.Single(Tabs.Tabs);
    }

    [Fact]
    public void UnregisteredMode_ShowsNotAvailable()
    {
        var session = Seed(Rows(), mode: ProjectMode.Compare);
        var cut = RenderTab(session);
        Assert.Contains("Compare is not available", cut.Markup);
    }

    [Fact]
    public void ModeSwitch_KeepsEnvironmentFilterAndSelection()
    {
        var session = Seed(Rows(), env: "dev");
        var cut = Open(session);
        session.SetFilter(session.Filter with { ChangedOnly = true });
        session.Select("Db:Port");
        cut.FindAll(".az-project-mode")[1].Click();
        Assert.Equal(ProjectMode.Compare, session.Mode);
        cut.FindAll(".az-project-mode")[0].Click();
        Assert.Equal((ProjectMode.Browse, "dev", true, "Db:Port"), (session.Mode, session.EnvironmentName, session.Filter.ChangedOnly, session.SelectedKey));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-path=\"Db:Port\"].selected")));
    }

    [Fact]
    public void Refresh_ReloadsTheBrowseView()
    {
        var session = Seed(Rows());
        var cut = Open(session);
        cut.Find("fluent-button[title^=Refresh]").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, Config.Loads.Count));
    }

    [Fact]
    public void Loading_ShowsTheProgressBarUnderTheHeader()
    {
        Config.Gate = new TaskCompletionSource();
        var session = Seed(Rows());
        var cut = RenderTab(session);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-project-progress")));
        Assert.NotEmpty(cut.FindAll(".az-browse-skeleton-row"));
        Config.Gate.SetResult();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".az-project-progress")));
    }

    [Fact]
    public void Render_ProjectMissingFromTheWorkspace_ShowsOneLine()
    {
        var session = Seed(Rows());
        Workspace.SetTree(new AzCfg.Ui.Application.Workspace.WorkspaceTree("C:/src", [], []));
        var cut = RenderTab(session);
        Assert.Contains("no longer in the workspace", cut.Markup);
    }

    [Fact]
    public void Secrets_NeverReachTheTabMarkup()
    {
        var session = Seed([Secret("Api:Key")]);
        var cut = Open(session);
        session.Select("Api:Key");
        Assert.DoesNotContain(Sentinel, cut.Markup);
    }
}
