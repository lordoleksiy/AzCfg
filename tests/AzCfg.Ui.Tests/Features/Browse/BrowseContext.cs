using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Browse;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Features.Project;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Browse.Fakes;
using AzCfg.Ui.Tests.Support;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Features.Browse;

/// <summary>
/// bUnit context with the platform, project and Browse features over fakes: one bound project "Orders" (service <c>orders</c>)
/// with the environments dev and prod (protected). The real <see cref="DraftService"/> sits behind a call log.
/// </summary>
public abstract class BrowseContext : UiTestContext
{
    protected BrowseContext()
    {
        var sync = new FakeSyncService { ExportJson = BrowseFixtures.EditJson(("Db:Host", "h1"), ("Db:Port", "1")) };
        Drafts = new RecordingDraftService(new DraftService(sync, new HmacSecretFingerprinter(new byte[32])));
        Sync = sync;
        Services.AddSingleton<TimeProvider>(Time);
        Services.AddSingleton<IOperationRunner>(Runner);
        Services.AddSingleton<IConfigurationService>(Config);
        Services.AddSingleton<IDraftService>(Drafts);
        Services.AddSingleton<ILocalValuesService>(LocalValues);
        Services.AddSingleton<ISecretRevealService>(Reveal);
        Services.AddSingleton<IWorkspaceService>(WorkspaceService);
        Services.AddSingleton<ISyncService>(sync);
        Services.AddSingleton<IActivationFlow>(Activation);
        Services.AddScoped<IBindingFlow, NotAvailableBindingFlow>();
        Services.AddScoped<WorkspaceState>();
        Services.AddScoped<ProjectSessions>();
        Services.AddScoped<ProtectedUnlockState>();
        Services.AddScoped<TabsState>();
        Services.AddScoped<WorkbenchNavigator>();
        Services.AddPlatformFeature();
        Services.AddProjectFeature();
        Services.AddBrowseFeature();
        Providers = Render<FluentProviders>();
    }

    /// <summary>Hosts dialogs and toasts.</summary>
    protected IRenderedComponent<FluentProviders> Providers { get; }

    internal FakeTimeProvider Time { get; } = new();

    internal FakeOperationRunner Runner { get; } = new();

    internal FakeConfigurationService Config { get; } = new();

    internal RecordingDraftService Drafts { get; }

    internal FakeSyncService Sync { get; }

    internal RecordingLocalValues LocalValues { get; } = new();

    internal FakeSecretReveal Reveal { get; } = new();

    internal Fakes.FakeWorkspaceService WorkspaceService { get; } = new();

    internal FakeActivationFlow Activation { get; } = new();

    protected WorkspaceState Workspace => Services.GetRequiredService<WorkspaceState>();

    protected ProjectSessions Sessions => Services.GetRequiredService<ProjectSessions>();

    protected TabsState Tabs => Services.GetRequiredService<TabsState>();

    protected ProtectedUnlockState Unlock => Services.GetRequiredService<ProtectedUnlockState>();

    protected ProjectBinding? CurrentBinding => Workspace.FindProject(BrowseFixtures.ProjectId)?.Binding;

    /// <summary>Seeds the workspace and Config rows; returns the project's session on <paramref name="env"/> in <paramref name="mode"/>.</summary>
    protected ProjectSession Seed(IReadOnlyList<ConfigRow> rows, string? active = "dev", string env = "dev", ProjectMode mode = ProjectMode.Browse, IReadOnlyList<string>? ignore = null)
    {
        Config.Rows = rows;
        Workspace.SetTree(new WorkspaceTree("C:/src", [], [BrowseFixtures.Node(BrowseFixtures.Binding(active, ignore))]));
        Workspace.SetEnvironments(new EnvironmentsSnapshot(null, [BrowseFixtures.Dev, BrowseFixtures.Prod]));
        var session = Sessions.GetOrCreate(BrowseFixtures.ProjectId);
        session.SetEnvironment(env);
        session.SetMode(mode);
        return session;
    }

    protected IRenderedComponent<BrowseView> RenderBrowse(ProjectSession session)
    {
        var cut = Render<BrowseView>(p => p.Add(c => c.Session, session));
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(session.View);
            Assert.Empty(cut.FindAll(".az-browse-skeleton"));
        });
        return cut;
    }

    protected IRenderedComponent<ProjectTab> RenderTab(ProjectSession session)
    {
        var id = Tabs.Open(new TabDescriptor(TabKind.Project, session.ProjectId), "Orders");
        var tab = Tabs.Tabs.First(t => t.Id == id);
        return Render<ProjectTab>(p => p.Add(c => c.Tab, tab));
    }

    protected static void ClickRow<T>(IRenderedComponent<T> cut, string key)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.Find($"[data-path=\"{key}\"]").Click();

    protected static void Press<T>(IRenderedComponent<T> cut, string key, string name)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.Find($"[data-path=\"{key}\"]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = name });

    protected static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> cut, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == text);

    protected static bool HasButton<T>(IRenderedComponent<T> cut, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("fluent-button").Any(b => b.TextContent.Trim() == text);
}
