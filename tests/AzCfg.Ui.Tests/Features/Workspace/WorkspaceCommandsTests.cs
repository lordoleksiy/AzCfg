using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Environments;
using AzCfg.Ui.Features.Workspace;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Workspace.Fakes;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Workspace.ShellFixtures;

namespace AzCfg.Ui.Tests.Features.Workspace;

public sealed class WorkspaceCommandsTests : ShellContext
{
    private readonly FakeBindingFlow _binding = new();

    public WorkspaceCommandsTests() => Services.AddScoped<IBindingFlow>(_ => _binding);

    private UiCommand Command(string id) => Commands.All.Single(c => c.Id == id);

    private void SeedProjects() =>
        SeedTree(Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api")), Node("Shop.Contracts", null, true))]));

    [Fact]
    public void Commands_AreContributedWithIdsOfTheRegistry()
    {
        var ids = Commands.All.Select(c => c.Id).ToList();
        Assert.Contains(CommandIds.WorkspaceOpenFolder, ids);
        Assert.Contains(CommandIds.WorkspaceRefresh, ids);
        Assert.Contains(CommandIds.ProjectBind, ids);
        Assert.Contains(CommandIds.ProjectSettings, ids);
        Assert.Contains(CommandIds.EnvSetup, ids);
        Assert.Contains(CommandIds.EnvManage, ids);
    }

    [Fact]
    public void Features_RegisterTheThreeSidebarSectionsInOrderAndTheSettingsTab()
    {
        var sections = Services.GetServices<SidebarSectionRegistration>().OrderBy(s => s.Order).Select(s => s.Id);
        Assert.Equal(["workspace", "environments", "resources"], sections);
        Assert.Contains(Services.GetServices<TabRegistration>(), t => t.Kind == TabKind.Settings && t.Component == typeof(SettingsTab));
        Assert.Contains(Services.GetServices<StatusBarItemRegistration>(), s => s.Component == typeof(AccountStatusItem));
    }

    [Fact]
    public void Refresh_NoFolder_IsDisabled()
    {
        Assert.False(Command(CommandIds.WorkspaceRefresh).IsEnabled!());
        SeedProjects();
        Assert.True(Command(CommandIds.WorkspaceRefresh).IsEnabled!());
    }

    [Fact]
    public async Task Refresh_ReScansTheOpenRootAndReplacesTheTree()
    {
        SeedProjects();
        Workspace.SelectProject(IdOf("Orders.Api"));
        WorkspaceService.Tree = Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api", active: "stage")))]);
        Assert.True(await Commands.ExecuteAsync(CommandIds.WorkspaceRefresh));
        Assert.Equal([Root], WorkspaceService.Opened);
        Assert.Equal("stage", Workspace.FindProject(IdOf("Orders.Api"))!.Binding!.ActiveEnvironment);
        Assert.Equal(IdOf("Orders.Api"), Workspace.SelectedProjectId);
        Assert.Contains((("Refresh " + Root), OperationKind.Read), Runner.Runs);
    }

    [Fact]
    public async Task Refresh_SelectedProjectGone_DropsTheSelection()
    {
        SeedProjects();
        Workspace.SelectProject(IdOf("Shop.Contracts"));
        WorkspaceService.Tree = Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api")))]);
        await Commands.ExecuteAsync(CommandIds.WorkspaceRefresh);
        Assert.Null(Workspace.SelectedProjectId);
    }

    [Fact]
    public async Task Refresh_ScanFails_KeepsTheTreeAndToasts()
    {
        _ = Providers;
        SeedProjects();
        WorkspaceService.OpenFailure = new IOException("Disk gone");
        await Commands.ExecuteAsync(CommandIds.WorkspaceRefresh);
        Assert.NotNull(Workspace.FindProject(IdOf("Orders.Api")));
        Providers.WaitForAssertion(() => Assert.Contains("Disk gone", Providers.Markup));
    }

    [Fact]
    public async Task Bind_NeedsABindableActiveProject_AndRunsTheFlow()
    {
        SeedProjects();
        Assert.False(Command(CommandIds.ProjectBind).IsEnabled!());
        Workspace.SelectProject(IdOf("Shop.Contracts"));
        Assert.False(Command(CommandIds.ProjectBind).IsEnabled!());
        Workspace.SelectProject(IdOf("Orders.Api"));
        Assert.True(Command(CommandIds.ProjectBind).IsEnabled!());
        await Commands.ExecuteAsync(CommandIds.ProjectBind);
        Assert.Equal([IdOf("Orders.Api")], _binding.Calls);
    }

    [Fact]
    public async Task Bind_ActiveProjectTabWinsOverTheSelection()
    {
        SeedProjects();
        Workspace.SelectProject(IdOf("Shop.Contracts"));
        Services.GetRequiredService<WorkbenchNavigator>().OpenProject(IdOf("Orders.Api"));
        await Commands.ExecuteAsync(CommandIds.ProjectBind);
        Assert.Equal([IdOf("Orders.Api")], _binding.Calls);
    }

    [Fact]
    public async Task ProjectSettings_OpensTheSettingsTab()
    {
        SeedProjects();
        Workspace.SelectProject(IdOf("Orders.Api"));
        await Commands.ExecuteAsync(CommandIds.ProjectSettings);
        Assert.Equal(TabKind.Settings, Tabs.Tabs.Single().Descriptor.Kind);
    }

    [Fact]
    public async Task EnvManage_OpensTheSettingsTab()
    {
        await Commands.ExecuteAsync(CommandIds.EnvManage);
        Assert.Equal(TabKind.Settings, Tabs.Tabs.Single().Descriptor.Kind);
    }

    [Fact]
    public async Task EnvSetup_OpensTheWizardDialog()
    {
        var providers = Providers;
        var task = Commands.ExecuteAsync(CommandIds.EnvSetup);
        providers.WaitForAssertion(() => Assert.Contains("One store per environment", providers.Markup));
        Click(providers, "Cancel");
        Assert.True(await task);
    }
}
