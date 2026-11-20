using System.Diagnostics;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Components.KeyTree;
using AzCfg.Ui.Features.Browse;
using AzCfg.Ui.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Browse.BrowseFixtures;
using CodeEditor = AzCfg.Ui.Components.Editors.CodeEditor;

namespace AzCfg.Ui.Tests.Features.Browse;

public sealed class BrowseViewTests : BrowseContext
{
    private static readonly DraftKey Host = new(ConfigScope.Service, "Db:Host");

    private static IReadOnlyList<ConfigRow> Rows() =>
    [
        Plain("Db:Host", "h1", "h1"),
        Plain("Db:Port", "1", "2", DiffKind.LocalModified),
        Secret("Api:Key"),
        Plain("Feature:On", "true", "true", scope: ConfigScope.Global),
        Ignored("Debug:Trace", "x"),
    ];

    [Fact]
    public void AddBrowseFeature_RegistersTheBrowseMode()
    {
        var registration = Assert.Single(new ServiceCollection().AddBrowseFeature().BuildServiceProvider().GetServices<ProjectModeRegistration>());
        Assert.Equal((ProjectMode.Browse, typeof(BrowseView)), (registration.Mode, registration.Component));
    }

    [Fact]
    public void Render_LoadsThroughTheRunnerAndCachesInTheSession()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        Assert.Equal(["Load dev"], Runner.Runs.Select(r => r.Title));
        Assert.All(Runner.Runs, r => Assert.Equal(AzCfg.Ui.Application.Operations.OperationKind.Read, r.Kind));
        Assert.Equal(5, session.View!.Rows.Count);
        Assert.NotNull(cut.Find("[data-path=\"Db:Host\"]"));

        Render<BrowseView>(p => p.Add(c => c.Session, session));
        Assert.Single(Config.Loads);
    }

    [Fact]
    public void Render_WhileLoading_ShowsSkeletonRows()
    {
        Config.Gate = new TaskCompletionSource();
        var session = Seed(Rows());
        var cut = Render<BrowseView>(p => p.Add(c => c.Session, session));
        Assert.Equal(8, cut.FindAll(".az-browse-skeleton-row").Count);
        Config.Gate.SetResult();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".az-browse-skeleton-row")));
    }

    [Fact]
    public void Invalidate_ReloadsTheView()
    {
        var session = Seed(Rows());
        RenderBrowse(session);
        session.Invalidate();
        Services.GetRequiredService<ProjectSessions>().GetOrCreate(ProjectId);
        Assert.Equal(["dev", "dev"], Config.Loads);
        Assert.NotNull(session.View);
    }

    [Fact]
    public void Render_LoadFailure_ShowsRetryAndRetryReloads()
    {
        Config.Failure = new InvalidOperationException("boom");
        var session = Seed(Rows());
        var cut = Render<BrowseView>(p => p.Add(c => c.Session, session));
        cut.WaitForAssertion(() => Assert.Contains("Could not load dev", cut.Markup));
        Config.Failure = null;
        cut.Find("fluent-message-bar fluent-button").Click();
        cut.WaitForAssertion(() => Assert.NotNull(session.View));
        Assert.Equal(2, Config.Loads.Count);
    }

    [Fact]
    public void Render_Unbound_ShowsBindPrompt()
    {
        var session = Seed(Rows());
        Workspace.SetTree(new WorkspaceTree("C:/src", [], [Node(null)]));
        var cut = Render<BrowseView>(p => p.Add(c => c.Session, session));
        Assert.Contains("Bind this project to a service", cut.Markup);
    }

    [Fact]
    public void Render_NoKeys_ShowsOneLineEmptyState()
    {
        var cut = RenderBrowse(Seed([]));
        Assert.Contains("No keys for ‘orders’ in dev", cut.Markup);
    }

    [Fact]
    public void Render_ShowsLocalAzureAndStatusColumns()
    {
        var cut = RenderBrowse(Seed(Rows()));
        var headers = cut.FindAll("[role=columnheader]").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(["Key", "Local", "Azure · dev", "Status", ""], headers);
        Assert.Contains("global", cut.Find("[data-path=\"Feature:On\"]").TextContent);
        Assert.Contains("M", cut.Find("[data-path=\"Db:Port\"] .az-diff-badge.modified").TextContent);
        Assert.Contains("ignored", cut.Find("[data-path=\"Debug:Trace\"]").TextContent);
        Assert.Contains("kv.vault.azure.net/orders--Api-Key", cut.Find("[data-path=\"Api:Key\"]").TextContent);
    }

    [Fact]
    public void Render_NonActiveEnvironment_IsRemoteOnlyWithoutLocalColumn()
    {
        var cut = RenderBrowse(Seed([Plain("Db:Host", "h1")], active: "dev", env: "prod"));
        Assert.True(Config.Loads is ["prod"]);
        var headers = cut.FindAll("[role=columnheader]").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(["Key", "Azure · prod", "Status", ""], headers);
        Assert.DoesNotContain("Changed", cut.FindAll("fluent-button").Select(b => b.TextContent.Trim()));
    }

    [Fact]
    public void Render_SecretSentinel_IsNeverInMarkupUntilReveal()
    {
        var session = Seed([Secret("Api:Key")]);
        var cut = RenderBrowse(session);
        ClickRow(cut, "Api:Key");
        Assert.DoesNotContain(Sentinel, cut.Markup);
        Assert.Contains("••••••", cut.Find("[data-path=\"Api:Key\"]").TextContent);

        cut.FindAll("button[aria-label=Reveal]")[0].Click();
        cut.WaitForAssertion(() => Assert.Contains(Sentinel, cut.Markup));
        Assert.Equal(1, Reveal.Local + Reveal.Remote);

        cut.FindAll("button[aria-label=Hide]")[0].Click();
        Assert.DoesNotContain(Sentinel, cut.Markup);
    }

    [Fact]
    public void Reveal_HidesAfterThirtySecondsAndOnTabChange()
    {
        var session = Seed([Secret("Api:Key")]);
        var cut = RenderBrowse(session);
        ClickRow(cut, "Api:Key");
        cut.FindAll("button[aria-label=Reveal]")[0].Click();
        cut.WaitForAssertion(() => Assert.Contains(Sentinel, cut.Markup));
        Time.Advance(TimeSpan.FromSeconds(31));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Sentinel, cut.Markup));

        cut.FindAll("button[aria-label=Reveal]")[0].Click();
        cut.WaitForAssertion(() => Assert.Contains(Sentinel, cut.Markup));
        Tabs.Open(new TabDescriptor(TabKind.Welcome), "Welcome");
        cut.WaitForAssertion(() => Assert.DoesNotContain(Sentinel, cut.Markup));
    }

    [Fact]
    public void SelectingAnotherKey_DoesNotKeepARevealedSecret()
    {
        var session = Seed([Secret("Api:Key"), Secret("Api:Other")]);
        var cut = RenderBrowse(session);
        ClickRow(cut, "Api:Key");
        cut.FindAll("button[aria-label=Reveal]")[0].Click();
        cut.WaitForAssertion(() => Assert.Contains(Sentinel, cut.Markup));
        ClickRow(cut, "Api:Other");
        Assert.DoesNotContain(Sentinel, cut.Markup);
    }

    [Fact]
    public void InlineEdit_EnterOnAPlainKey_CreatesTheDraftAndRaisesPendingCount()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        ClickRow(cut, "Db:Host");
        Press(cut, "Db:Host", "Enter");
        var input = cut.Find("input[aria-label=\"Value of Db:Host\"]");
        input.Input("h2");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal(1, session.PendingCount("dev")));
        Assert.Equal(["Start dev", "SetValue Service:Db:Host=h2"], Drafts.Calls);
        Assert.Empty(cut.FindAll("input[aria-label=\"Value of Db:Host\"]"));
        Assert.Contains("→ h2", cut.Find("[data-path=\"Db:Host\"]").TextContent);
        Assert.NotEmpty(cut.Find("[data-path=\"Db:Host\"]").QuerySelectorAll(".az-diff-badge.draft"));
    }

    [Fact]
    public void InlineEdit_SecondEdit_ExtendsTheSameDraft()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        foreach (var (key, value) in new[] { ("Db:Host", "h2"), ("Db:Port", "9") })
        {
            ClickRow(cut, key);
            Press(cut, key, "Enter");
            var input = cut.Find($"input[aria-label=\"Value of {key}\"]");
            input.Input(value);
            input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
            cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("input[aria-label^=\"Value of\"]")));
        }

        Assert.Equal(2, session.PendingCount("dev"));
        Assert.Single(Drafts.Calls, c => c.StartsWith("Start", StringComparison.Ordinal));
    }

    [Fact]
    public void InlineEdit_Escape_CancelsWithoutADraft()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        ClickRow(cut, "Db:Host");
        Press(cut, "Db:Host", "Enter");
        cut.Find("input[aria-label=\"Value of Db:Host\"]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("input[aria-label=\"Value of Db:Host\"]"));
        Assert.Null(session.DraftFor("dev"));
        Assert.Empty(Drafts.Calls);
    }

    [Fact]
    public void ProtectedAndLocked_DisablesEditActions_UntilUnlocked()
    {
        var session = Seed([Plain("Db:Host", "h1", "h1")], active: "prod", env: "prod");
        var cut = RenderBrowse(session);
        ClickRow(cut, "Db:Host");
        Press(cut, "Db:Host", "Enter");
        Assert.Empty(cut.FindAll("input[aria-label=\"Value of Db:Host\"]"));
        foreach (var text in new[] { "Edit", "Set secret value", "Move to Key Vault", "Delete" })
        {
            Assert.True(Button(cut, text).HasAttribute("disabled"), text);
        }

        Assert.True(Button(cut, "+ Add key").HasAttribute("disabled"));
        Unlock.Unlock("prod");
        cut.WaitForAssertion(() => Assert.False(Button(cut, "Edit").HasAttribute("disabled")));
        Assert.False(Button(cut, "+ Add key").HasAttribute("disabled"));
    }

    [Fact]
    public void Inspector_Delete_RemovesTheKeyInTheDraft()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        ClickRow(cut, "Db:Host");
        Button(cut, "Delete").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, session.PendingCount("dev")));
        Assert.Contains("Remove Service:Db:Host", Drafts.Calls);
        Assert.Contains("deleted", cut.Find("[data-path=\"Db:Host\"]").TextContent);
    }

    [Fact]
    public void Inspector_Revert_RestoresTheKeyAndDropsTheEmptyDraft()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        ClickRow(cut, "Db:Host");
        Button(cut, "Delete").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, session.PendingCount("dev")));
        Button(cut, "Revert").Click();
        cut.WaitForAssertion(() => Assert.Null(session.DraftFor("dev")));
        Assert.Contains("Revert Service:Db:Host", Drafts.Calls);
        Assert.Equal(0, session.TotalPending);
    }

    [Fact]
    public void Inspector_MoveToKeyVault_CallsTheDraftService()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        ClickRow(cut, "Db:Host");
        Button(cut, "Move to Key Vault").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, session.PendingCount("dev")));
        Assert.Contains("MoveToKeyVault Service:Db:Host", Drafts.Calls);
        Assert.DoesNotContain("h1", cut.Find("[data-path=\"Db:Host\"] .az-browse-draft").TextContent);
    }

    [Fact]
    public void Inspector_SetSecretValue_UsesAPasswordInputAndNeverRendersTheValue()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        ClickRow(cut, "Db:Host");
        Button(cut, "Set secret value").Click();
        var input = cut.Find("input[aria-label=\"New secret value\"]");
        Assert.Equal("password", input.GetAttribute("type"));
        input.Input(Sentinel);
        Button(cut, "Save secret").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, session.PendingCount("dev")));
        Assert.Contains("SetSecretValue Service:Db:Host", Drafts.Calls);
        Assert.DoesNotContain(Sentinel, cut.Markup);
        Assert.DoesNotContain(Sentinel, string.Join('\n', Drafts.Calls));
        Assert.DoesNotContain(Sentinel, session.DraftFor("dev")!.Json);
        Assert.Empty(cut.FindAll("input[aria-label=\"New secret value\"]"));
    }

    [Fact]
    public void Inspector_ShowsPendingBeforeAndAfter_AndWarnings()
    {
        Config.Warnings = [new SyncWarning(SyncWarningKind.LabelOverride, "Db:Host", "Overrides an unlabeled key")];
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        ClickRow(cut, "Db:Host");
        Assert.Contains("Overrides an unlabeled key", cut.Markup);
        Button(cut, "Edit").Click();
        cut.Find("input[aria-label=\"Azure value\"]").Input("h9");
        Button(cut, "Save").Click();
        cut.WaitForAssertion(() => Assert.Equal("h9", cut.Find(".az-bi-after").TextContent.Trim()));
        Assert.Equal("h1", cut.Find(".az-bi-before").TextContent.Trim());
    }

    [Fact]
    public void AddKey_CreatesADraftEditForTheNewKey()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        Button(cut, "+ Add key").Click();
        cut.Find("input[aria-label=\"New key\"]").Input("Cache:Ttl");
        cut.Find("input[aria-label=\"New value\"]").Input("60");
        Button(cut, "Add").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, session.PendingCount("dev")));
        Assert.Contains("SetValue Service:Cache:Ttl=60", Drafts.Calls);
        Assert.Contains("→ 60", cut.Find("[data-path=\"Cache:Ttl\"]").TextContent);
        Assert.Equal("Cache:Ttl", session.SelectedKey);
    }

    [Fact]
    public async Task JsonView_ValidEdit_UpdatesTheDraft()
    {
        Config.RemoteJson = EditJson(("Db:Host", "h1"), ("Db:Port", "1"));
        var session = Seed(Rows());
        session.SetJsonView(true);
        var cut = RenderBrowse(session);
        var editor = cut.FindComponent<CodeEditor>();
        Assert.Equal(Config.RemoteJson, editor.Instance.Value);

        await cut.InvokeAsync(() => editor.Instance.OnChange.InvokeAsync(EditJson(("Db:Host", "h2"), ("Db:Port", "1"))));
        cut.WaitForAssertion(() => Assert.Equal(1, session.PendingCount("dev")));
        Assert.Contains("ReplaceJson", Drafts.Calls);
    }

    [Fact]
    public async Task JsonView_InvalidJson_KeepsTheDraftUnchanged()
    {
        Config.RemoteJson = EditJson(("Db:Host", "h1"), ("Db:Port", "1"));
        var session = Seed(Rows());
        session.SetJsonView(true);
        var cut = RenderBrowse(session);
        var editor = cut.FindComponent<CodeEditor>();
        await cut.InvokeAsync(() => editor.Instance.OnChange.InvokeAsync(EditJson(("Db:Host", "h2"), ("Db:Port", "1"))));
        cut.WaitForAssertion(() => Assert.Equal(1, session.PendingCount("dev")));
        var before = session.DraftFor("dev");

        Drafts.Diagnostics = [new JsonDiagnostic("Invalid JSON syntax.", null, 1, 5)];
        await cut.InvokeAsync(() => editor.Instance.OnChange.InvokeAsync("{ not json"));

        Assert.Same(before, session.DraftFor("dev"));
        Assert.Equal(1, session.PendingCount("dev"));
    }

    [Fact]
    public void JsonView_LocalSide_IsReadOnlyWithTheLocalJson()
    {
        Config.LocalJson = "{\"Api\":{\"Key\":\"***\"}}";
        var session = Seed(Rows());
        session.SetJsonView(true);
        var cut = RenderBrowse(session);
        Button(cut, "Local").Click();
        var editor = cut.FindComponent<CodeEditor>();
        Assert.True(editor.Instance.ReadOnly);
        Assert.Equal(Config.LocalJson, editor.Instance.Value);
        Assert.DoesNotContain(Sentinel, cut.Markup);
    }

    [Fact]
    public void JsonView_AzureSide_IsReadOnlyWhileProtectedAndLocked()
    {
        var session = Seed(Rows(), active: "prod", env: "prod");
        session.SetJsonView(true);
        var cut = RenderBrowse(session);
        Assert.True(cut.FindComponent<CodeEditor>().Instance.ReadOnly);
        Unlock.Unlock("prod");
        cut.WaitForAssertion(() => Assert.False(cut.FindComponent<CodeEditor>().Instance.ReadOnly));
    }

    [Fact]
    public void LocalActions_CallTheLocalValuesServiceAndInvalidate()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        ClickRow(cut, "Db:Host");
        Button(cut, "Remove local").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, Config.Loads.Count));
        Button(cut, "Edit local").Click();
        cut.Find("input[aria-label=\"Local value\"]").Input("local2");
        Button(cut, "Save").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, Config.Loads.Count));
        Button(cut, "Mark as global").Click();
        cut.WaitForAssertion(() => Assert.Equal(4, Config.Loads.Count));
        Assert.Equal(["Remove Db:Host", "SetPlain Db:Host=local2", "SetScope Db:Host Global"], LocalValues.Calls);
    }

    [Fact]
    public void LocalActions_ASecretIsWrittenThroughSetSecret()
    {
        var session = Seed([Secret("Api:Key")]);
        var cut = RenderBrowse(session);
        ClickRow(cut, "Api:Key");
        Button(cut, "Edit local").Click();
        var input = cut.Find("input[aria-label=\"Local value\"]");
        Assert.Equal("password", input.GetAttribute("type"));
        input.Input(Sentinel);
        Button(cut, "Save").Click();
        cut.WaitForAssertion(() => Assert.Equal(["SetSecret Api:Key"], LocalValues.Calls));
        Assert.DoesNotContain(Sentinel, cut.Markup);
    }

    [Fact]
    public void ContextMenu_IgnoreLocally_AddsTheKeyAndUpdatesTheBinding()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        cut.Find("[data-path=\"Db:Host\"] .az-browse-more").Click();
        cut.FindAll("[role=menuitem]").First(b => b.TextContent == "Ignore locally").Click();
        cut.WaitForAssertion(() => Assert.Equal(["Add Db:Host"], WorkspaceService.Calls));
        Assert.Equal(["Db:Host"], CurrentBinding!.Ignore);
        cut.WaitForAssertion(() => Assert.Equal(2, Config.Loads.Count));
    }

    [Fact]
    public void ContextMenu_StopIgnoring_RemovesTheRule()
    {
        var session = Seed(Rows(), ignore: ["Debug:Trace"]);
        var cut = RenderBrowse(session);
        cut.Find("[data-path=\"Debug:Trace\"] .az-browse-more").Click();
        cut.FindAll("[role=menuitem]").First(b => b.TextContent == "Stop ignoring").Click();
        cut.WaitForAssertion(() => Assert.Equal(["Remove Debug:Trace"], WorkspaceService.Calls));
    }

    [Fact]
    public void ContextMenu_ListsTheActionsOfTheKey_AndCopyKeyUsesTheClipboard()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        cut.Find("[data-path=\"Db:Host\"] .az-browse-more").Click();
        var items = cut.FindAll("[role=menuitem]").Select(i => i.TextContent.Trim()).ToList();
        Assert.Equal(
            ["Edit value", "Set secret value…", "Move to Key Vault", "Revert", "Delete", "Copy key", "Copy value", "Ignore locally", "Mark as global", "Compare across environments"],
            items);
        cut.FindAll("[role=menuitem]").First(i => i.TextContent == "Copy key").Click();
        JSInterop.VerifyInvoke("navigator.clipboard.writeText");
        Assert.Empty(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public void ContextMenu_CopyValue_IsDisabledForSecrets()
    {
        var cut = RenderBrowse(Seed(Rows()));
        cut.Find("[data-path=\"Api:Key\"] .az-browse-more").Click();
        Assert.True(cut.FindAll("[role=menuitem]").First(i => i.TextContent == "Copy value").HasAttribute("disabled"));
    }

    [Fact]
    public void ContextMenu_CompareAcrossEnvironments_SwitchesModeKeepingTheKey()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        cut.Find("[data-path=\"Db:Port\"] .az-browse-more").Click();
        cut.FindAll("[role=menuitem]").First(i => i.TextContent.StartsWith("Compare", StringComparison.Ordinal)).Click();
        Assert.Equal((ProjectMode.Compare, "Db:Port"), (session.Mode, session.SelectedKey));
    }

    [Fact]
    public void ContextMenu_RightClickOnAValueCell_OpensTheMenu()
    {
        var cut = RenderBrowse(Seed(Rows()));
        cut.Find("[data-path=\"Db:Host\"] .az-browse-cell").TriggerEvent("oncontextmenu", new MouseEventArgs { ClientX = 10, ClientY = 20 });
        Assert.NotEmpty(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public void Search_FiltersKeysAndValues_ButNeverSecretValues()
    {
        var cut = RenderBrowse(Seed(Rows()));
        cut.Find("input[type=search]").Input("h1");
        Assert.Equal(["Db:Host"], cut.FindAll("[role=row][data-path]").Select(r => r.GetAttribute("data-path")).Where(p => p!.Contains(':')));
        cut.Find("input[type=search]").Input("Port");
        Assert.NotEmpty(cut.FindAll("[data-path=\"Db:Port\"]"));
        Assert.Empty(cut.FindAll("[data-path=\"Db:Host\"]"));
        cut.Find("input[type=search]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.NotEmpty(cut.FindAll("[data-path=\"Db:Host\"]"));
    }

    [Fact]
    public void Toggles_ChangedSecretsIgnored_AndScope_UpdateTheSessionFilter()
    {
        var session = Seed(Rows());
        var cut = RenderBrowse(session);
        Button(cut, "Changed").Click();
        Assert.True(session.Filter.ChangedOnly);
        Assert.Empty(cut.FindAll("[data-path=\"Db:Host\"]"));
        Button(cut, "Changed").Click();
        Button(cut, "Secrets").Click();
        Assert.True(session.Filter.SecretsOnly);
        Button(cut, "Secrets").Click();
        Button(cut, "Ignored").Click();
        Assert.True(session.Filter.IgnoredOnly);
        Button(cut, "Ignored").Click();
        cut.Find("select[aria-label=Scope]").Change("global");
        Assert.Equal(ConfigScope.Global, session.Filter.Scope);
        Assert.Empty(cut.FindAll("[data-path=\"Db:Host\"]"));
    }

    [Fact]
    public async Task Browse_Commands_RunTheHandlersOfTheActiveBrowseTab()
    {
        var session = Seed(Rows());
        var tabId = Tabs.Open(new TabDescriptor(TabKind.Project, ProjectId), "Orders");
        Assert.Equal(tabId, Tabs.ActiveId);
        var cut = RenderBrowse(session);
        var commands = Services.GetRequiredService<ICommandRegistry>();
        session.Select("Db:Host");

        Assert.True(await commands.ExecuteAsync(CommandIds.BrowseDelete));
        cut.WaitForAssertion(() => Assert.Equal(1, session.PendingCount("dev")));
        Assert.Contains("Remove Service:Db:Host", Drafts.Calls);

        Assert.True(await commands.ExecuteAsync(CommandIds.BrowseAddKey));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input[aria-label=\"New key\"]")));
        Assert.True(await commands.ExecuteAsync(CommandIds.BrowseEdit));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input[aria-label=\"Value of Db:Host\"]")));

        session.SetMode(ProjectMode.Compare);
        Assert.False(await commands.ExecuteAsync(CommandIds.BrowseDelete));
    }

    [Fact]
    public async Task Refresh_Command_DropsTheViewAndReloads()
    {
        var session = Seed(Rows());
        Tabs.Open(new TabDescriptor(TabKind.Project, ProjectId), "Orders");
        RenderBrowse(session);
        Assert.True(await Services.GetRequiredService<ICommandRegistry>().ExecuteAsync(CommandIds.ProjectRefresh));
        Assert.Equal(2, Config.Loads.Count);
    }

    [Fact]
    public void LargeProject_FilteringAndModelBuildAreFast_AndOnlyAWindowIsRendered()
    {
        var rows = Enumerable.Range(0, 2000).Select(i => Plain($"Group{i % 50}:Key{i}", $"value-{i}", $"value-{i}")).ToList();
        var warm = ConfigFilters.Apply(rows, new ConfigFilter(Text: "value-1"));
        _ = new KeyTreeModel<ConfigRow>(warm, r => r.Key, null, true).Flatten();
        var clock = Stopwatch.StartNew();
        var filtered = ConfigFilters.Apply(rows, new ConfigFilter(Text: "value-19"));
        var flat = new KeyTreeModel<ConfigRow>(filtered, r => r.Key, r => r.Status is not null and not DiffKind.Unchanged, true).Flatten();
        clock.Stop();
        Assert.True(clock.ElapsedMilliseconds < 100, $"filter took {clock.ElapsedMilliseconds} ms");
        Assert.NotEmpty(flat);

        var cut = RenderBrowse(Seed(rows));
        Assert.True(cut.FindAll("[role=row][data-path]").Count < 2000);
    }
}
