using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Hosting;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Components.Layout.Fakes;
using AzCfg.Ui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Components.Layout;

/// <summary>bUnit context with the platform feature, fakes of the singleton services and test registrations.</summary>
public abstract class WorkbenchContext : UiTestContext
{
    protected WorkbenchContext(ConnectionState connection = ConnectionState.Connected, UiHostOptions? hostOptions = null)
    {
        Runner = new FakeOperationRunner();
        Status = new FakeAzureStatusService(connection);
        Workspace = new FakeWorkspaceService();
        Services.AddSingleton<IOperationRunner>(Runner);
        Services.AddSingleton<IAzureStatusService>(Status);
        Services.AddSingleton<IWorkspaceRegistry>(new FakeWorkspaceRegistry(new WorkspaceEntry("C:/src/Shop", "Shop", DateTimeOffset.UnixEpoch, false)));
        Services.AddSingleton<IWorkspaceService>(Workspace);
        Services.AddSingleton(hostOptions ?? new UiHostOptions());
        Services.AddSingleton(TimeProvider.System);
        Services.AddScoped<WorkspaceState>();
        Services.AddScoped<ProjectSessions>();
        Services.AddScoped<ProtectedUnlockState>();
        Services.AddScoped<TabsState>();
        Services.AddScoped<WorkbenchNavigator>();
        Services.AddPlatformFeature();
        Services.AddTab(TabKind.Project, typeof(TestTab));
        Services.AddTab(TabKind.Settings, typeof(ThrowingTab));
        Services.AddSidebarSection("test", "Tests", typeof(TestSection), 5);
        Services.AddStatusBarItem(typeof(TestStatusItem), StatusBarAlignment.Right, 1);
    }

    internal FakeOperationRunner Runner { get; }

    internal FakeAzureStatusService Status { get; }

    internal FakeWorkspaceService Workspace { get; }

    protected TabsState Tabs => Services.GetRequiredService<TabsState>();

    protected static TabDescriptor Project(string name) => new(TabKind.Project, $"C:/src/{name}/{name}.csproj");
}
