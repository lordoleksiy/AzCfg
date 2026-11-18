using AzCfg.Core.Model;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.State;

namespace AzCfg.Ui.Tests.Features.Platform;

public sealed class PaletteSearchTests
{
    private const string SecretSentinel = "AZCFG-TEST-SECRET-7f3a";

    private readonly TabsState _tabs = new();
    private readonly ProjectSessions _sessions = new();
    private readonly WorkspaceState _workspace = new();
    private readonly CommandRegistry _registry;
    private readonly PaletteSearch _search;
    private int _ran;

    public PaletteSearchTests()
    {
        _registry = new CommandRegistry([new TestCommands(() => _ran++)]);
        _search = new PaletteSearch(_registry, _workspace, _sessions, _tabs, new WorkbenchNavigator(_tabs, _sessions, _workspace));
    }

    private sealed class TestCommands(Action run) : ICommandContributor
    {
        public IEnumerable<UiCommand> Commands =>
        [
            new("workbench.toggleSidebar", "Toggle sidebar", "Workbench", "Ctrl+B", () => { run(); return Task.CompletedTask; }),
            new("workbench.closeTab", "Close tab", "Workbench", "Alt+W", () => { run(); return Task.CompletedTask; }, () => false),
            new("draft.review", "Review pending edits", "Draft", null, () => { run(); return Task.CompletedTask; }),
        ];
    }

    private void OpenWorkspace()
    {
        var orders = StateFixtures.Node("Orders");
        var billing = StateFixtures.Node("Billing");
        _workspace.SetTree(new WorkspaceTree("C:/src", [], [orders, billing]));
        _workspace.SetEnvironments(new EnvironmentsSnapshot(null, [StateFixtures.Dev, new ConfigEnvironment("prod", new Uri("https://prod.azconfig.io"), null, true)]));
    }

    private ProjectSession OpenProjectWithKeys(params ConfigRow[] rows)
    {
        OpenWorkspace();
        var projectId = StateFixtures.Node("Orders").Id;
        _tabs.Open(new TabDescriptor(TabKind.Project, projectId), "Orders");
        var session = _sessions.GetOrCreate(projectId);
        session.SetView(new ConfigurationView(StateFixtures.Binding(), StateFixtures.Dev, false, rows, [], DateTimeOffset.UnixEpoch));
        return session;
    }

    private static ConfigRow Row(string key, bool secret = false, string? value = null) =>
        new(key, null, secret, false, new LocalCell(value, secret, StateFixtures.Binding().ValuesTarget, ConfigScope.Service), null);

    [Fact]
    public void Search_EmptyInput_ListsCommandsRankedWithShortcuts()
    {
        var items = _search.Search("", PaletteMode.Commands);
        Assert.Equal(["Toggle sidebar", "Close tab", "Review pending edits"], items.Select(i => i.Label));
        Assert.Equal("Ctrl+B", items[0].Shortcut);
    }

    [Fact]
    public void Search_FuzzyQuery_RanksBestFirst()
    {
        var items = _search.Search("rev", PaletteMode.Commands);
        Assert.Equal("Review pending edits", items[0].Label);
    }

    [Fact]
    public void Search_GreaterThanPrefix_BehavesLikeCommands() =>
        Assert.Equal(_search.Search("tog", PaletteMode.Commands).Select(i => i.Label), _search.Search(">tog", PaletteMode.QuickOpen).Select(i => i.Label));

    [Fact]
    public async Task Run_DisabledCommand_IsListedDisabledAndNotExecuted()
    {
        var item = Assert.Single(_search.Search("close", PaletteMode.Commands));
        Assert.False(item.Enabled);
        await item.Run();
        Assert.Equal(0, _ran);
    }

    [Fact]
    public async Task Run_EnabledCommand_Executes()
    {
        await _search.Search("toggle", PaletteMode.Commands)[0].Run();
        Assert.Equal(1, _ran);
    }

    [Fact]
    public async Task Search_HashPrefix_ListsProjectsAndOpensTheTab()
    {
        OpenWorkspace();
        var item = Assert.Single(_search.Search("#ord", PaletteMode.Commands));
        Assert.Equal("Orders", item.Label);
        await item.Run();
        Assert.Equal(StateFixtures.Node("Orders").Id, _tabs.Tabs.Single().Descriptor.ProjectId);
    }

    [Fact]
    public async Task Search_AtPrefix_ListsKeysOfActiveSessionAndSelectsThem()
    {
        var session = OpenProjectWithKeys(Row("Db:ConnectionString"), Row("Logging:Level"));
        var item = Assert.Single(_search.Search("@conn", PaletteMode.Commands));
        Assert.Equal("Db:ConnectionString", item.Label);
        await item.Run();
        Assert.Equal("Db:ConnectionString", session.SelectedKey);
        Assert.Equal(ProjectMode.Browse, session.Mode);
    }

    [Fact]
    public void Search_AtPrefix_NoProjectTab_IsEmpty()
    {
        OpenWorkspace();
        Assert.Empty(_search.Search("@", PaletteMode.Commands));
    }

    [Fact]
    public void Search_Keys_NeverListSecretValues()
    {
        OpenProjectWithKeys(Row("Api:Key", secret: true, value: SecretSentinel), Row("Plain", value: SecretSentinel));
        var items = _search.Search("@", PaletteMode.Commands);
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, i => $"{i.Label}{i.Detail}{i.Shortcut}".Contains(SecretSentinel, StringComparison.Ordinal));
        Assert.Equal("secret", items.Single(i => i.Label == "Api:Key").Detail);
    }

    [Fact]
    public async Task Search_EnvPrefix_ListsEnvironmentsAndSwitchesTheActiveSession()
    {
        var session = OpenProjectWithKeys();
        var items = _search.Search("env:pr", PaletteMode.Commands);
        var prod = Assert.Single(items);
        Assert.Equal("prod", prod.Label);
        Assert.Equal("protected", prod.Detail);
        await prod.Run();
        Assert.Equal("prod", session.EnvironmentName);
    }

    [Fact]
    public void Search_EnvPrefix_WithoutProjectTab_ListsEnvironmentsDisabled()
    {
        OpenWorkspace();
        Assert.All(_search.Search("env:", PaletteMode.Commands), i => Assert.False(i.Enabled));
    }

    [Fact]
    public void Search_QuickOpen_ListsProjectsThenKeys()
    {
        OpenProjectWithKeys(Row("Orders:Timeout"));
        var items = _search.Search("orders", PaletteMode.QuickOpen);
        Assert.Equal(["Orders", "Orders:Timeout"], items.Select(i => i.Label));
    }

    [Fact]
    public void Search_ManyKeys_IsCappedAtMaxItems()
    {
        OpenProjectWithKeys([.. Enumerable.Range(0, 500).Select(i => Row($"Key{i}"))]);
        Assert.Equal(PaletteSearch.MaxItems, _search.Search("@", PaletteMode.Commands).Count);
    }
}
