using AzCfg.Core.Environments;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Environments;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Features.Workspace;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Environments.Fakes;
using AzCfg.Ui.Tests.Features.Workspace.Fakes;
using AzCfg.Ui.Tests.Support;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Features.Workspace;

/// <summary>
/// bUnit context for U1: the platform, Workspace and Environments features over fakes of the Application interfaces.
/// The default environments are dev and prod (protected); <see cref="Providers"/> hosts dialogs and toasts.
/// </summary>
public abstract class ShellContext : UiTestContext
{
    private IRenderedComponent<FluentProviders>? _providers;

    protected ShellContext(ConnectionState connection = ConnectionState.Connected)
    {
        Status = new FakeAzureStatusService(connection);
        Services.AddSingleton<IOperationRunner>(Runner);
        Services.AddSingleton<IAzureStatusService>(Status);
        Services.AddSingleton<IWorkspaceService>(WorkspaceService);
        Services.AddSingleton<IWorkspaceRegistry>(Registry);
        Services.AddSingleton<IDirectoryBrowser>(Browser);
        Services.AddSingleton<IEnvironmentService>(EnvService);
        Services.AddSingleton<IProjectSettingsService>(ProjectSettings);
        Services.AddSingleton<ISyncWorkflowService>(Sync);
        Services.AddSingleton<IActivationFlow>(Activation);
        Services.AddScoped<WorkspaceState>();
        Services.AddScoped<ProjectSessions>();
        Services.AddScoped<ProtectedUnlockState>();
        Services.AddScoped<TabsState>();
        Services.AddScoped<WorkbenchNavigator>();
        Services.AddPlatformFeature();
        Services.AddWorkspaceFeature();
        Services.AddEnvironmentsFeature();
        var profile = ShellFixtures.Profile(ShellFixtures.Dev, ShellFixtures.Prod);
        EnvService.Snapshot = new EnvironmentsSnapshot(profile, new ProfileEnvironmentResolver().Resolve(profile));
    }

    /// <summary>Hosts dialogs and toasts; rendered on first use, so a test registers its own services before touching it.</summary>
    protected IRenderedComponent<FluentProviders> Providers => _providers ??= Render<FluentProviders>();

    internal FakeOperationRunner Runner { get; } = new();

    internal FakeAzureStatusService Status { get; }

    internal FakeWorkspaceService WorkspaceService { get; } = new();

    internal FakeWorkspaceRegistry Registry { get; } = new(new WorkspaceEntry("C:/src/Shop", "Shop", DateTimeOffset.UnixEpoch, true), new WorkspaceEntry("C:/src/Blog", "Blog", DateTimeOffset.UnixEpoch, false));

    internal FakeDirectoryBrowser Browser { get; } = new();

    internal FakeEnvironmentService EnvService { get; } = new();

    internal FakeProjectSettingsService ProjectSettings { get; } = new();

    internal FakeSyncWorkflowService Sync { get; } = new();

    internal FakeActivationFlow Activation { get; } = new();

    protected WorkspaceState Workspace => Services.GetRequiredService<WorkspaceState>();

    protected ProjectSessions Sessions => Services.GetRequiredService<ProjectSessions>();

    protected TabsState Tabs => Services.GetRequiredService<TabsState>();

    protected ICommandRegistry Commands => Services.GetRequiredService<ICommandRegistry>();

    /// <summary>Publishes the saved environments (as the sidebar does after loading) without running anything.</summary>
    protected void SeedEnvironments() => Workspace.SetEnvironments(EnvService.Snapshot);

    protected void SeedTree(WorkspaceTree tree) => Workspace.SetTree(tree);

    protected static AngleSharp.Dom.IElement ButtonText<T>(IRenderedComponent<T> cut, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == text);

    protected static void Click<T>(IRenderedComponent<T> cut, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.WaitForAssertion(() => ButtonText(cut, text).Click());

    protected static AngleSharp.Dom.IElement ButtonTitled<T>(IRenderedComponent<T> cut, string title)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("fluent-button").First(b => b.GetAttribute("title") == title);
}
