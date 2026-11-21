using AzCfg.Core.Health;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Health;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Features.Problems;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Sync;
using AzCfg.Ui.Tests.Features.Sync.Fakes;
using AzCfg.Ui.Tests.Support;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Features.Problems;

/// <summary>bUnit context with the platform and Problems features over a fake <see cref="IProblemsService"/>; one bound project "Orders".</summary>
public abstract class ProblemsContext : UiTestContext
{
    protected ProblemsContext()
    {
        Services.AddSingleton<IOperationRunner>(Runner);
        Services.AddSingleton<IProblemsService>(Problems);
        Services.AddScoped<WorkspaceState>();
        Services.AddScoped<ProjectSessions>();
        Services.AddScoped<ProtectedUnlockState>();
        Services.AddScoped<TabsState>();
        Services.AddScoped<WorkbenchNavigator>();
        Services.AddScoped<IBindingFlow, NotAvailableBindingFlow>();
        Services.AddPlatformFeature();
        Services.AddProblemsFeature();
        Render<FluentProviders>();
        Workspace.SetTree(new WorkspaceTree("C:/src", [], [SyncFixtures.Node(SyncFixtures.Binding())]));
        Workspace.SelectProject(SyncFixtures.ProjectId);
    }

    internal FakeOperationRunner Runner { get; } = new();

    internal FakeProblemsService Problems { get; } = new();

    protected WorkspaceState Workspace => Services.GetRequiredService<WorkspaceState>();

    protected ProjectSessions Sessions => Services.GetRequiredService<ProjectSessions>();

    protected TabsState Tabs => Services.GetRequiredService<TabsState>();

    protected static ProblemRow Row(HealthSeverity severity, HealthCheckKind kind, string message, string? environment = null, string? key = null, string? fix = null) =>
        new(severity, kind, message, environment, key, fix);

    protected static void Click<T>(IRenderedComponent<T> cut, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.WaitForAssertion(() => cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == text).Click());
}
