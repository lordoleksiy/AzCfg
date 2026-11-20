using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Features.Sync;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Sync.Fakes;
using AzCfg.Ui.Tests.Support;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Features.Sync;

/// <summary>bUnit context with the platform and Sync features over fakes; one bound project "Orders" with envs dev and prod.</summary>
public abstract class SyncContext : UiTestContext
{
    protected SyncContext()
    {
        Services.AddSingleton<IOperationRunner>(Runner);
        Services.AddSingleton<ISyncWorkflowService>(Workflow);
        Services.AddSingleton<IDraftService>(Drafts);
        Services.AddSingleton<IWorkspaceService>(WorkspaceService);
        Services.AddScoped<WorkspaceState>();
        Services.AddScoped<ProjectSessions>();
        Services.AddScoped<ProtectedUnlockState>();
        Services.AddScoped<TabsState>();
        Services.AddScoped<WorkbenchNavigator>();
        Services.AddScoped<IBindingFlow, NotAvailableBindingFlow>();
        Services.AddPlatformFeature();
        Services.AddSyncFeature();
        Providers = Render<FluentProviders>();
    }

    /// <summary>Hosts dialogs and toasts.</summary>
    protected IRenderedComponent<FluentProviders> Providers { get; }

    internal FakeOperationRunner Runner { get; } = new();

    internal FakeSyncWorkflowService Workflow { get; } = new();

    internal FakeDraftService Drafts { get; } = new();

    internal FakeWorkspaceService WorkspaceService { get; } = new();

    protected WorkspaceState Workspace => Services.GetRequiredService<WorkspaceState>();

    protected ProjectSessions Sessions => Services.GetRequiredService<ProjectSessions>();

    protected ProjectBinding? CurrentBinding => Workspace.FindProject(SyncFixtures.ProjectId)?.Binding;

    /// <summary>Seeds the workspace and returns the project's session in Sync mode on <paramref name="env"/>.</summary>
    protected ProjectSession Seed(ProjectBinding? binding, string env = "dev", SyncView view = SyncView.Push)
    {
        Workspace.SetTree(new WorkspaceTree("C:/src", [], [SyncFixtures.Node(binding)]));
        Workspace.SetEnvironments(new EnvironmentsSnapshot(null, [SyncFixtures.Dev, SyncFixtures.Prod]));
        var session = Sessions.GetOrCreate(SyncFixtures.ProjectId);
        session.SetEnvironment(env);
        session.SetMode(ProjectMode.Sync);
        session.SetSyncView(view);
        return session;
    }

    protected IRenderedComponent<SyncMode> RenderSync(ProjectSession session) => Render<SyncMode>(p => p.Add(c => c.Session, session));

    protected static AngleSharp.Dom.IElement ButtonStartingWith<T>(IRenderedComponent<T> cut, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("fluent-button").First(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal));

    protected static void Click<T>(IRenderedComponent<T> cut, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.WaitForAssertion(() => cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == text).Click());
}
