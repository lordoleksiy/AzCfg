using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.Security;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Compare;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Compare.Fakes;
using AzCfg.Ui.Tests.Support;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using DiffEditor = AzCfg.Ui.Components.Editors.DiffEditor;

namespace AzCfg.Ui.Tests.Features.Compare;

public sealed class CompareViewTests : UiTestContext
{
    private const string Sentinel = "AZCFG-TEST-SECRET-7f3a";

    private static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly ConfigEnvironment Stage = new("stage", new Uri("https://stage.azconfig.io"), null, false);

    private readonly FakeCompareService _compare = new();
    private readonly FakeOperationRunner _runner = new();
    private readonly WorkspaceState _workspace = new();
    private readonly ProjectSession _session;

    public CompareViewTests()
    {
        Services.AddSingleton<ICompareService>(_compare);
        Services.AddSingleton<IOperationRunner>(_runner);
        Services.AddSingleton(_workspace);
        Services.AddScoped<LayoutState>();
        var node = Node(Binding("dev"));
        _workspace.SetTree(new WorkspaceTree("C:/src", [], [node]));
        _workspace.SetEnvironments(new EnvironmentsSnapshot(new UserProfile([Dev, Stage]), [Dev, Stage]));
        _session = new ProjectSession(node.Id);
        _session.SetEnvironment("dev");
        _session.SetMode(ProjectMode.Compare);
        _compare.Rows =
        [
            Row("Db:Host", CompareResult.Different, "a", "b"),
            Row("Db:Port", CompareResult.Same, "5432", "5432"),
            new("Features:New", CompareResult.OnlyLeft, Plain("on"), null),
            new("Features:Old", CompareResult.OnlyRight, null, Plain("off")),
        ];
        _compare.IgnoredCount = 2;
    }

    private static ProjectBinding Binding(string? active)
    {
        var target = new LocalTargetBinding(LocalTargetKind.UserSecrets, "C:/src/Crm/Crm.csproj", LocalFileFormat.NestedJson);
        return new ProjectBinding("C:/src/Crm", "crm", target, target, new Dictionary<string, Uri>(), active);
    }

    private static ProjectNode Node(ProjectBinding? binding) =>
        new(new WorkspaceProject(new ProjectInfo("C:/src/Crm", "C:/src/Crm/Crm.csproj", "Crm", false, null), []), binding);

    private static CompareCell Plain(string value) => new(value, false, null, ConfigScope.Service, null);

    private static CompareRow Row(string key, CompareResult result, string left, string right) => new(key, result, Plain(left), Plain(right));

    private IRenderedComponent<CompareView> RenderView() => Render<CompareView>(p => p.Add(c => c.Session, _session));

    private static List<string> ButtonTexts(IRenderedComponent<CompareView> cut) => [.. cut.FindAll("fluent-button").Select(b => b.TextContent.Trim())];

    private static void ClickButton(IRenderedComponent<CompareView> cut, string text) =>
        cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == text).Click();

    [Fact]
    public void AddCompareFeature_RegistersTheCompareMode()
    {
        var services = new ServiceCollection().AddCompareFeature().BuildServiceProvider();
        var registration = Assert.Single(services.GetServices<ProjectModeRegistration>());
        Assert.Equal((ProjectMode.Compare, typeof(CompareView)), (registration.Mode, registration.Component));
    }

    [Fact]
    public void Render_SelectedEnvIsActive_ComparesLocalWithItAndCachesInSession()
    {
        RenderView();
        Assert.Equal((new CompareSide.Local(), new CompareSide.Environment(Dev)), Assert.Single(_compare.Calls));
        Assert.Equal(new CompareSide.Local(), _session.CompareLeft);
        Assert.NotNull(_session.Comparison);
        Assert.Contains("Compare Local ↔ dev", _runner.Titles);
    }

    [Fact]
    public void Render_SelectedEnvIsNotActive_ComparesActiveWithSelected()
    {
        _session.SetEnvironment("stage");
        RenderView();
        Assert.Equal((new CompareSide.Environment(Dev), new CompareSide.Environment(Stage)), Assert.Single(_compare.Calls));
    }

    [Fact]
    public void Render_CachedComparison_DoesNotReload()
    {
        var view = new ComparisonView(Binding("dev"), new CompareSide.Local(), new CompareSide.Environment(Dev), [], 0, "{}", "{}", DateTimeOffset.UnixEpoch);
        _session.SetComparison(view.Left, view.Right, view);
        RenderView();
        Assert.Empty(_compare.Calls);
    }

    [Fact]
    public void Click_Swap_ReloadsWithSidesExchanged()
    {
        var cut = RenderView();
        cut.Find("fluent-button[title='Swap sides']").Click();
        cut.WaitForAssertion(() => Assert.Equal((new CompareSide.Environment(Dev), new CompareSide.Local()), _compare.Calls[^1]));
        Assert.Equal(new CompareSide.Environment(Dev), _session.CompareLeft);
    }

    [Fact]
    public void Change_LeftPicker_ReloadsWithTheChosenEnvironment()
    {
        var cut = RenderView();
        cut.Find("select[aria-label='Left side']").Change("stage");
        cut.WaitForAssertion(() => Assert.Equal((new CompareSide.Environment(Stage), new CompareSide.Environment(Dev)), _compare.Calls[^1]));
    }

    [Fact]
    public void Render_ShowsCountsAndIgnoredHint()
    {
        var cut = RenderView();
        Assert.Contains("1 different · 1 only left · 1 only right", cut.Find(".az-compare-counts").TextContent);
        Assert.Contains("2 ignored", cut.Find(".az-compare-counts").TextContent);
    }

    [Fact]
    public void DifferencesOnly_OnByDefault_HidesSameRowsUntilTurnedOff()
    {
        var cut = RenderView();
        Assert.NotEmpty(cut.FindAll("[data-path='Db:Host']"));
        Assert.Empty(cut.FindAll("[data-path='Db:Port']"));
        ClickButton(cut, "Differences only");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-path='Db:Port']")));
    }

    [Fact]
    public void Render_ResultColumn_ShowsGlyphs()
    {
        var cut = RenderView();
        var results = cut.FindAll(".az-compare-result").Select(e => e.TextContent.Trim()).ToList();
        Assert.Contains("≠", results);
        Assert.Contains("←", results);
        Assert.Contains("→", results);
    }

    [Fact]
    public void Search_FiltersRowsByKey()
    {
        var cut = RenderView();
        cut.Find("input[type='search']").Input("New");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-path='Db:Host']")));
        Assert.NotEmpty(cut.FindAll("[data-path='Features:New']"));
        Assert.Equal("New", _session.Filter.Text);
    }

    [Fact]
    public void Render_NoDifferences_ShowsEmptyState()
    {
        _compare.Rows = [Row("Db:Port", CompareResult.Same, "1", "1")];
        var cut = RenderView();
        Assert.Contains("No differences", cut.Markup);
    }

    [Fact]
    public void Render_Secrets_ShowNameOrMaskAndNeverTheValue()
    {
        _compare.Rows =
        [
            new("Db:Password", CompareResult.Different, new CompareCell(Sentinel, true, null, ConfigScope.Service, null),
                new CompareCell(Sentinel, true, "crm--Db--Password", ConfigScope.Service, null)),
        ];
        _session.Select("Db:Password");
        var cut = RenderView();
        Assert.DoesNotContain(Sentinel, cut.Markup);
        Assert.Contains("crm--Db--Password", cut.Markup);
        Assert.Contains(SecretPolicy.Mask, cut.Markup);
        Assert.Contains("Different secret value", cut.Markup);
    }

    [Fact]
    public void JsonView_PassesLeftAndRightJsonToTheDiffEditor()
    {
        _compare.LeftJson = "{\"Db\":{\"Host\":\"a\"}}";
        _compare.RightJson = "{\"Db\":{\"Host\":\"b\"}}";
        var cut = RenderView();
        ClickButton(cut, "JSON diff");
        var editor = cut.WaitForComponent<DiffEditor>().Instance;
        Assert.Equal((_compare.LeftJson, _compare.RightJson), (editor.Original, editor.Modified));
        Assert.True(_session.JsonView);
    }

    [Fact]
    public void Invalidate_AfterAWrite_ReloadsTheComparison()
    {
        var cut = RenderView();
        cut.InvokeAsync(_session.Invalidate);
        cut.WaitForAssertion(() => Assert.Equal(2, _compare.Calls.Count));
        Assert.NotNull(_session.Comparison);
    }

    [Fact]
    public void SetEnvironment_ReloadsWithTheNewDefaultSides()
    {
        var cut = RenderView();
        cut.InvokeAsync(() => _session.SetEnvironment("stage"));
        cut.WaitForAssertion(() => Assert.Equal((new CompareSide.Environment(Dev), new CompareSide.Environment(Stage)), _compare.Calls[^1]));
        Assert.Equal(new CompareSide.Environment(Stage), _session.Comparison?.Right);
    }

    [Fact]
    public void Inspector_ShowsBothSidesAndOpenInBrowseSwitchesMode()
    {
        _session.Select("Db:Host");
        var cut = RenderView();
        var inspector = cut.Find(".az-inspector").TextContent;
        Assert.Contains("Different values", inspector);
        Assert.Contains("Local", inspector);
        Assert.Contains("dev", inspector);
        ClickButton(cut, "Open in Browse");
        Assert.Equal((ProjectMode.Browse, "Db:Host"), (_session.Mode, _session.SelectedKey));
    }

    [Fact]
    public void Click_Row_SelectsTheKeyInTheSession()
    {
        var cut = RenderView();
        cut.Find("[data-path='Db:Host']").Click();
        Assert.Equal("Db:Host", _session.SelectedKey);
    }

    [Fact]
    public void Failure_ShowsErrorBarAndRetryReloads()
    {
        _compare.Failure = new InvalidOperationException("boom");
        var cut = RenderView();
        Assert.Contains("Retry", ButtonTexts(cut));
        _compare.Failure = null;
        ClickButton(cut, "Retry");
        cut.WaitForAssertion(() => Assert.NotNull(_session.Comparison));
        Assert.Equal(2, _compare.Calls.Count);
    }

    [Fact]
    public void Render_LocalOnBothSides_DoesNotCompare()
    {
        _session.SetComparison(new CompareSide.Local(), new CompareSide.Local(), null);
        var cut = RenderView();
        Assert.Empty(_compare.Calls);
        Assert.Contains("Choose an environment on at least one side", cut.Markup);
    }

    [Fact]
    public void Render_UnboundProject_ShowsEmptyStateWithoutComparing()
    {
        _workspace.SetTree(new WorkspaceTree("C:/src", [], [Node(null)]));
        var cut = RenderView();
        Assert.Empty(_compare.Calls);
        Assert.Contains("Bind this project to a service", cut.Markup);
    }
}
