using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Workspace;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Workspace.ShellFixtures;

namespace AzCfg.Ui.Tests.Features.Workspace;

public sealed class OpenFolderDialogTests : ShellContext
{
    private WorkspaceActions Actions => Services.GetRequiredService<WorkspaceActions>();

    [Fact]
    public void Render_ListsRootsWithTheDotNetMarkerAndRecentFolders()
    {
        var cut = Render<OpenFolderDialog>();
        Assert.Equal(["src", "docs"], cut.FindAll(".az-of-name").Take(2).Select(e => e.TextContent));
        Assert.Single(cut.FindAll(".az-of-net"));
        Assert.Equal(["Shop", "Blog"], cut.FindAll(".az-of-recent-row .az-of-name").Select(e => e.TextContent));
        Assert.Contains((("List folders"), OperationKind.Read), Runner.Runs);
    }

    [Fact]
    public void Click_Folder_ListsItAndFillsThePath()
    {
        Browser.Listings["C:/src"] = new DirectoryListing("C:/src", "C:/", [new DirectoryItem("Shop", "C:/src/Shop", true)]);
        var cut = Render<OpenFolderDialog>();
        cut.FindAll(".az-of-item").First(b => b.TextContent.Contains("src", StringComparison.Ordinal)).Click();
        Assert.Equal("C:/src", cut.Find("input[aria-label='Folder path']").GetAttribute("value"));
        Assert.Contains("Shop", cut.FindAll(".az-of-name").Select(e => e.TextContent));
        Assert.Contains("..", cut.FindAll(".az-of-item span").Select(e => e.TextContent));
    }

    [Fact]
    public void Input_Path_OffersCompletions()
    {
        Browser.Completions = ["C:/src", "C:/srv"];
        var cut = Render<OpenFolderDialog>();
        cut.Find("input[aria-label='Folder path']").Input("C:/s");
        Assert.Equal(["C:/src", "C:/srv"], cut.FindAll("#az-of-completions option").Select(o => o.GetAttribute("value")));
    }

    [Fact]
    public void Render_PathEmpty_OpenIsDisabled() =>
        Assert.True(ButtonText(Render<OpenFolderDialog>(), "Open").HasAttribute("disabled"));

    [Fact]
    public async Task OpenFolder_Scanned_ReturnsTheTreeAndStoresItThroughAReadOperation()
    {
        var providers = Providers;
        var task = Actions.OpenFolderAsync();
        providers.WaitForAssertion(() => Assert.Contains("Recent and pinned", providers.Markup));
        providers.WaitForAssertion(() => providers.FindAll(".az-of-recent-row .az-of-item").First(b => b.TextContent.Contains("Shop", StringComparison.Ordinal)).Click());
        Assert.True(await task);
        Assert.Equal(["C:/src/Shop"], WorkspaceService.Opened);
        Assert.Equal("C:/src/Shop", Workspace.Tree!.RootPath);
        Assert.Contains((("Open C:/src/Shop"), OperationKind.Read), Runner.Runs);
    }

    [Fact]
    public async Task OpenFolder_TypedPath_OpensIt()
    {
        var providers = Providers;
        var task = Actions.OpenFolderAsync();
        providers.WaitForAssertion(() => Assert.Contains("Folder path", providers.Markup));
        providers.Find("input[aria-label='Folder path']").Input("C:/work/Api");
        Click(providers, "Open");
        Assert.True(await task);
        Assert.Equal("C:/work/Api", Workspace.Tree!.RootPath);
    }

    [Fact]
    public async Task OpenFolder_Cancelled_KeepsTheCurrentTree()
    {
        SeedTree(Tree([]));
        var providers = Providers;
        var task = Actions.OpenFolderAsync();
        providers.WaitForAssertion(() => Assert.Contains("Folder path", providers.Markup));
        Click(providers, "Cancel");
        Assert.False(await task);
        Assert.Empty(WorkspaceService.Opened);
        Assert.Equal(Root, Workspace.Tree!.RootPath);
    }

    [Fact]
    public async Task Cancel_WhileScanning_CancelsTheScanAndKeepsTheDialogOpen()
    {
        WorkspaceService.OpenGate = new TaskCompletionSource();
        var cut = Render<OpenFolderDialog>();
        cut.Find("input[aria-label='Folder path']").Input("C:/big");
        var open = cut.InvokeAsync(() => ButtonText(cut, "Open").Click());
        cut.WaitForAssertion(() => Assert.Contains("Scanning C:/big", cut.Markup));
        Assert.True(ButtonText(cut, "Open").HasAttribute("disabled"));
        Click(cut, "Cancel");
        await open;
        Assert.True(WorkspaceService.LastOpenToken.IsCancellationRequested);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Scanning", cut.Markup));
        Assert.Null(Workspace.Tree);
    }

    [Fact]
    public async Task Dispose_WhileScanning_CancelsTheScan()
    {
        WorkspaceService.OpenGate = new TaskCompletionSource();
        var cut = Render<OpenFolderDialog>();
        cut.Find("input[aria-label='Folder path']").Input("C:/big");
        var open = cut.InvokeAsync(() => ButtonText(cut, "Open").Click());
        cut.WaitForAssertion(() => Assert.Contains("Scanning", cut.Markup));
        await cut.InvokeAsync(() => cut.Instance.Dispose());
        Assert.True(WorkspaceService.LastOpenToken.IsCancellationRequested);
        await open;
    }

    [Fact]
    public void Open_ScanFails_ShowsTheErrorInline()
    {
        WorkspaceService.OpenFailure = new IOException("Access denied");
        var cut = Render<OpenFolderDialog>();
        cut.Find("input[aria-label='Folder path']").Input("C:/locked");
        Click(cut, "Open");
        cut.WaitForAssertion(() => Assert.Contains("Access denied", cut.Markup));
    }

    [Fact]
    public void PinAndRemove_ChangeTheRecentList()
    {
        var cut = Render<OpenFolderDialog>();
        ButtonTitled(cut, "Pin Blog").Click();
        cut.WaitForAssertion(() => Assert.NotNull(ButtonTitled(cut, "Unpin Blog")));
        ButtonTitled(cut, "Remove Shop from the list").Click();
        cut.WaitForAssertion(() => Assert.Equal(["Blog"], cut.FindAll(".az-of-recent-row .az-of-name").Select(e => e.TextContent)));
        Assert.Contains(Runner.Runs, r => r.Title == "Remove folder from the list" && r.Kind == OperationKind.Write);
    }
}
