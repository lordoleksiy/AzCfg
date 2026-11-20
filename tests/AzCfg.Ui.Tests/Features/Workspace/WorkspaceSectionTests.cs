using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Workspace;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Workspace.Fakes;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using static AzCfg.Ui.Tests.Features.Workspace.ShellFixtures;

namespace AzCfg.Ui.Tests.Features.Workspace;

public sealed class WorkspaceSectionTests : ShellContext
{
    private readonly FakeBindingFlow _binding = new();

    public WorkspaceSectionTests() => Services.AddScoped<IBindingFlow>(_ => _binding);

    private WorkspaceTree SampleTree() => Tree(
        [Solution("Shop", Node("Orders.Api", Binding("Orders.Api")), Node("Billing", null), Node("Shop.Contracts", null, true))],
        Node("Tools", null));

    private IRenderedComponent<FluentTreeItem> Item(IRenderedComponent<WorkspaceSection> cut, string textStart) =>
        cut.FindComponents<FluentTreeItem>().First(i => i.Instance.Text!.StartsWith(textStart, StringComparison.Ordinal));

    private static Task Select(IRenderedComponent<FluentTreeItem> item) => item.InvokeAsync(() => item.Instance.SelectedChanged.InvokeAsync(true));

    [Fact]
    public void Render_NoFolder_ShowsOneLineAndOpenAction()
    {
        var opened = new List<string>();
        Commands.Register(new UiCommand(CommandIds.WorkspaceOpenFolder, "Open", "Workspace", null, () => { opened.Add("open"); return Task.CompletedTask; }));
        var cut = Render<WorkspaceSection>();
        Assert.Contains("Open a folder to start", cut.Markup);
        Click(cut, "Open…");
        Assert.Single(opened);
    }

    [Fact]
    public void Render_Tree_ListsSolutionsProjectsAndLooseProjects()
    {
        SeedTree(SampleTree());
        var cut = Render<WorkspaceSection>();
        var texts = cut.FindComponents<FluentTreeItem>().Select(i => i.Instance.Text!).ToList();
        Assert.Contains("Shop", texts);
        Assert.Contains("Orders.Api · dev", texts);
        Assert.Contains("Billing · unbound", texts);
        Assert.Contains("Shop.Contracts", texts);
        Assert.Contains("Projects", texts);
        Assert.Contains("Tools · unbound", texts);
    }

    [Fact]
    public void Render_Library_IsDimmedAndHasNoStatusSuffix()
    {
        SeedTree(SampleTree());
        var library = Item(Render<WorkspaceSection>(), "Shop.Contracts");
        Assert.Equal("Shop.Contracts", library.Instance.Text);
        Assert.Contains("az-ws-dim", library.Markup);
    }

    [Fact]
    public void Render_ProjectInSeveralSolutions_AppearsUnderEach()
    {
        var shared = Node("Shared");
        SeedTree(Tree([Solution("A", shared), Solution("B", shared)]));
        var cut = Render<WorkspaceSection>();
        Assert.Equal(2, cut.FindComponents<FluentTreeItem>().Count(i => i.Instance.Text!.StartsWith("Shared", StringComparison.Ordinal)));
    }

    [Fact]
    public void Render_ConfigFiles_ShowRolesAndLocalSettingsReadOnlyWithTooltip()
    {
        var files = new[] { File("Orders.Api", ConfigFileKind.AppSettings), File("Orders.Api", ConfigFileKind.AppSettingsLocal), File("Orders.Api", ConfigFileKind.UserSecrets), File("Orders.Api", ConfigFileKind.LocalSettings) };
        SeedTree(Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api"), false, files))]));
        var cut = Render<WorkspaceSection>();
        var texts = cut.FindComponents<FluentTreeItem>().Select(i => i.Instance.Text!).ToList();
        Assert.Contains("appsettings.json", texts);
        Assert.Contains("appsettings.local.json · values", texts);
        Assert.Contains("user-secrets · secrets", texts);
        var localSettings = Item(cut, "local.settings.json");
        Assert.Equal("local.settings.json (read-only)", localSettings.Instance.Text);
        Assert.Contains("title=\"read-only, not supported yet\"", localSettings.Markup);
    }

    [Fact]
    public void Render_LoadedView_ShowsCountersOfTheActiveEnvironment()
    {
        SeedTree(SampleTree());
        var cut = Render<WorkspaceSection>();
        var session = Sessions.GetOrCreate(IdOf("Orders.Api"));
        cut.InvokeAsync(() => session.SetView(new ConfigurationView(Binding("Orders.Api"), Dev, false,
        [
            new ConfigRow("A", DiffKind.LocalAdded, false, false, null, null),
            new ConfigRow("B", DiffKind.RemoteModified, false, false, null, null),
            new ConfigRow("C", DiffKind.Conflict, false, false, null, null),
        ], [], DateTimeOffset.UnixEpoch)));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindComponents<FluentTreeItem>(), i => i.Instance.Text == "Orders.Api · dev ↑1 ↓1 !1"));
    }

    [Fact]
    public async Task Select_BoundProject_SelectsAndOpensItsTab()
    {
        SeedTree(SampleTree());
        var cut = Render<WorkspaceSection>();
        await Select(Item(cut, "Orders.Api"));
        Assert.Equal(IdOf("Orders.Api"), Workspace.SelectedProjectId);
        Assert.Equal(new TabDescriptor(TabKind.Project, IdOf("Orders.Api")), Tabs.Tabs.Single().Descriptor);
        Assert.Empty(_binding.Calls);
    }

    [Fact]
    public async Task Select_UnboundProject_StartsBindingAndOpensTabAfterwards()
    {
        SeedTree(SampleTree());
        var cut = Render<WorkspaceSection>();
        await Select(Item(cut, "Billing"));
        Assert.Equal([IdOf("Billing")], _binding.Calls);
        Assert.Single(Tabs.Tabs);
    }

    [Fact]
    public async Task Select_UnboundProjectWhenBindingIsCancelled_OnlySelects()
    {
        _binding.Result = false;
        SeedTree(SampleTree());
        var cut = Render<WorkspaceSection>();
        await Select(Item(cut, "Billing"));
        Assert.Equal(IdOf("Billing"), Workspace.SelectedProjectId);
        Assert.Empty(Tabs.Tabs);
    }

    [Fact]
    public async Task Select_Library_OnlySelects()
    {
        SeedTree(SampleTree());
        var cut = Render<WorkspaceSection>();
        await Select(Item(cut, "Shop.Contracts"));
        Assert.Empty(Tabs.Tabs);
        Assert.Empty(_binding.Calls);
        Assert.Contains("nothing to bind", cut.Markup);
    }

    [Fact]
    public void Render_SelectedProject_OffersBindOrBindingSettings()
    {
        SeedTree(SampleTree());
        Workspace.SelectProject(IdOf("Billing"));
        var cut = Render<WorkspaceSection>();
        Assert.Contains("Bind…", cut.FindAll("fluent-button").Select(b => b.TextContent.Trim()));
        cut.InvokeAsync(() => Workspace.SelectProject(IdOf("Orders.Api")));
        cut.WaitForAssertion(() => Assert.Contains("Binding settings…", cut.FindAll("fluent-button").Select(b => b.TextContent.Trim())));
    }

    [Fact]
    public async Task OpenSolution_NewFolder_ExpandsSolutionsOnly()
    {
        SeedTree(SampleTree());
        var cut = Render<WorkspaceSection>();
        Assert.True(Item(cut, "Shop").Instance.Expanded);
        Assert.False(Item(cut, "Orders.Api").Instance.Expanded);
        await cut.InvokeAsync(() => ButtonTitled(cut, "Collapse all").Click());
        Assert.False(Item(cut, "Shop").Instance.Expanded);
    }

    [Fact]
    public void ToolbarButtons_ExecuteTheWorkspaceCommands()
    {
        var ran = new List<string>();
        foreach (var id in new[] { CommandIds.WorkspaceOpenFolder, CommandIds.WorkspaceRefresh })
        {
            Commands.Register(new UiCommand(id, id, "Workspace", null, () => { ran.Add(id); return Task.CompletedTask; }));
        }

        SeedTree(SampleTree());
        var cut = Render<WorkspaceSection>();
        ButtonTitled(cut, "Open folder").Click();
        ButtonTitled(cut, "Refresh").Click();
        Assert.Equal([CommandIds.WorkspaceOpenFolder, CommandIds.WorkspaceRefresh], ran);
    }
}
