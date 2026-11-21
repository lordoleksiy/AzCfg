using AzCfg.Core.Model;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Compare;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Tests.Features.Compare.Fakes;
using AzCfg.Ui.Tests.Support;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using RecordingDraftService = AzCfg.Ui.Tests.Features.Browse.Fakes.RecordingDraftService;

namespace AzCfg.Ui.Tests.Features.Compare;

public sealed class CompareMatrixTests : UiTestContext
{
    private const string Sentinel = "AZCFG-TEST-SECRET-7f3a";

    private static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    private static readonly ConfigEnvironment Stage = new("stage", new Uri("https://stage.azconfig.io"), null, false);
    private static readonly ConfigEnvironment Prod = new("prod", new Uri("https://prod.azconfig.io"), null, true);

    private readonly FakeCompareService _compare = new();
    private readonly WorkspaceState _workspace = new();
    private readonly ProjectSession _session;
    private readonly RecordingDraftService _drafts;
    private readonly IRenderedComponent<FluentProviders> _providers;

    public CompareMatrixTests()
    {
        Services.AddSingleton<ICompareService>(_compare);
        Services.AddSingleton<IOperationRunner>(new FakeOperationRunner());
        Services.AddSingleton(_workspace);
        Services.AddScoped<LayoutState>();
        _drafts = CompareCopyServices.Add(Services);
        _providers = Render<FluentProviders>();
        var target = new LocalTargetBinding(LocalTargetKind.UserSecrets, "C:/src/Crm/Crm.csproj", LocalFileFormat.NestedJson);
        var binding = new ProjectBinding("C:/src/Crm", "crm", target, target, new Dictionary<string, Uri>(), "dev");
        var node = new ProjectNode(new WorkspaceProject(new ProjectInfo("C:/src/Crm", "C:/src/Crm/Crm.csproj", "Crm", false, null), []), binding);
        _workspace.SetTree(new WorkspaceTree("C:/src", [], [node]));
        _workspace.SetEnvironments(new EnvironmentsSnapshot(new UserProfile([Dev, Stage, Prod]), [Dev, Stage, Prod]));
        _session = new ProjectSession(node.Id);
        _session.SetEnvironment("dev");
        _session.SetMode(ProjectMode.Compare);
        _compare.Matrix = new MissingKeysView(binding, [Dev, Stage, Prod],
        [
            Row("Api:Url", false, Plain("u"), Plain("u"), Plain("u")),
            Row("Db:Host", true, Plain("h"), Plain("h"), null),
            Row("Features:New", true, Plain("on"), null, null),
            Row("Db:Password", true, Secret("crm--Db--Password"), null, null),
            Row("Shared:Flag", true, Plain("x"), null, null, ConfigScope.Global),
        ], 2, DateTimeOffset.UnixEpoch);
    }

    private static MatrixRow Row(string key, bool gap, MatrixCell? dev, MatrixCell? stage, MatrixCell? prod, ConfigScope scope = ConfigScope.Service) =>
        new(key, scope, gap, [dev, stage, prod]);

    private static MatrixCell Plain(string value) => new(value, false, null);

    private static MatrixCell Secret(string name) => new(Sentinel, true, name);

    private IRenderedComponent<CompareView> RenderMatrix()
    {
        var cut = Render<CompareView>(p => p.Add(c => c.Session, _session));
        cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Matrix").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-matrix-table")));
        return cut;
    }

    private static void Click(IRenderedComponent<CompareView> cut, string text) =>
        cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == text).Click();

    private static AngleSharp.Dom.IElement Cell(IRenderedComponent<CompareView> cut, string key, int column) =>
        cut.Find($"tr[data-path='{key}']").QuerySelectorAll("td")[column];

    [Fact]
    public void Matrix_ListsGapsOnlyByDefaultWithCountsAndPerEnvironmentMissing()
    {
        var cut = RenderMatrix();
        Assert.Empty(cut.FindAll("tr[data-path='Api:Url']"));
        Assert.Equal(4, cut.FindAll("tbody tr").Count);
        Assert.Contains("4 of 5 keys with gaps", cut.Markup);
        Assert.Contains("2 ignored", cut.Markup);
        var headers = cut.FindAll("thead th").Select(h => h.TextContent.Trim()).ToList();
        Assert.Contains("dev 0 missing", headers);
        Assert.Contains("stage 3 missing", headers);
        Assert.Contains("prod 4 missing", headers);
        Assert.Equal(1, _compare.MatrixCalls);

        Click(cut, "Gaps only");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tr[data-path='Api:Url']")));
    }

    [Fact]
    public void Matrix_Search_FiltersKeys()
    {
        var cut = RenderMatrix();
        cut.Find("input[type='search']").Input("Features");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
    }

    [Fact]
    public void CopyFrom_AddsThePlainValueToTheTargetDraft()
    {
        var cut = RenderMatrix();
        cut.WaitForAssertion(() => Assert.Contains("Copy from dev", Cell(cut, "Features:New", 1).TextContent));
        Cell(cut, "Features:New", 1).QuerySelector("fluent-button")!.Click();
        cut.WaitForAssertion(() => Assert.Equal(1, _session.PendingCount("stage")));
        Assert.Contains("SetValue Service:Features:New=on", _drafts.Calls);
        Assert.Contains("pending (draft)", Cell(cut, "Features:New", 1).TextContent);
        Assert.Equal(0, _session.PendingCount("dev"));
    }

    [Fact]
    public void CopyFrom_ProtectedTarget_RequiresUnlockFirst()
    {
        var cut = RenderMatrix();
        Cell(cut, "Db:Host", 2).QuerySelector("fluent-button")!.Click();
        _providers.WaitForAssertion(() => Assert.Contains("prod is protected", _providers.Markup));
        Assert.Empty(_drafts.Calls);
        Assert.Null(_session.DraftFor("prod"));

        _providers.WaitForAssertion(() => _providers.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Unlock").Click());
        cut.WaitForAssertion(() => Assert.Equal(1, _session.PendingCount("prod")));
        Assert.Contains("SetValue Service:Db:Host=h", _drafts.Calls);
    }

    [Fact]
    public void CopyFrom_ProtectedTarget_CancelledUnlockLeavesNoDraft()
    {
        var cut = RenderMatrix();
        Cell(cut, "Db:Host", 2).QuerySelector("fluent-button")!.Click();
        _providers.WaitForAssertion(() => _providers.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Cancel").Click());
        Assert.Empty(_drafts.Calls);
        Assert.Equal(0, _session.PendingCount("prod"));
    }

    [Fact]
    public void MissingSecret_OffersSetSecretValue_TypedValueGoesToTheDraftAndNeverIntoMarkup()
    {
        var cut = RenderMatrix();
        Assert.DoesNotContain("Copy from", Cell(cut, "Db:Password", 1).TextContent);
        Cell(cut, "Db:Password", 1).QuerySelector("fluent-button")!.Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-matrix-secret")));
        cut.Find("input[aria-label='New secret value']").Input(Sentinel + "-new");
        cut.Find(".az-matrix-secret").QuerySelectorAll("fluent-button").First(b => b.TextContent.Trim() == "Set secret value").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, _session.PendingCount("stage")));
        Assert.Contains("SetSecretValue Service:Db:Password", _drafts.Calls);
        Assert.Empty(cut.FindAll(".az-matrix-secret"));
        Assert.DoesNotContain(Sentinel, cut.Markup);
        Assert.DoesNotContain(Sentinel, string.Join("|", _drafts.Calls));
    }

    [Fact]
    public void Matrix_NeverRendersSecretValues_ShowsNames()
    {
        var cut = RenderMatrix();
        Assert.DoesNotContain(Sentinel, cut.Markup);
        Assert.Contains("crm--Db--Password", Cell(cut, "Db:Password", 0).TextContent);
    }

    [Fact]
    public void GlobalKeys_AreNotCopied()
    {
        var cut = RenderMatrix();
        Assert.Contains("(global)", cut.Find("tr[data-path='Shared:Flag']").TextContent);
        Assert.Empty(cut.Find("tr[data-path='Shared:Flag']").QuerySelectorAll("fluent-button"));
    }
}
