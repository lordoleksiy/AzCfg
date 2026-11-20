using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using Bunit;
using static AzCfg.Ui.Tests.Features.Sync.SyncFixtures;

namespace AzCfg.Ui.Tests.Features.Sync;

public sealed class PushViewTests : SyncContext
{
    private static AngleSharp.Dom.IElement Apply(IRenderedComponent<AzCfg.Ui.Features.Sync.SyncMode> cut) => ButtonStartingWith(cut, "Apply");

    [Fact]
    public void Render_NonActiveEnvironment_DisablesPushAndPullAndOffersMakeActive()
    {
        var cut = RenderSync(Seed(Binding("dev"), env: "prod"));
        Assert.All(cut.FindAll("button.az-sync-tab"), b => Assert.True(b.HasAttribute("disabled")));
        Assert.Contains("Make prod active to sync local files", cut.Markup);
        Assert.Empty(Workflow.PushPreviews);
    }

    [Fact]
    public void Render_ActiveEnvironment_SelectsEveryChange()
    {
        var cut = RenderSync(Seed(Binding()));
        Assert.Contains("2 of 2 changes", cut.Markup);
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Uncheck_AllChanges_DisablesApplyWithReason()
    {
        var cut = RenderSync(Seed(Binding()));
        cut.Find("input.az-sync-all").Change(false);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Contains("Nothing selected", Apply(cut).GetAttribute("title"));
    }

    [Fact]
    public void Uncheck_OneChange_SelectsTheChosenKeys()
    {
        var cut = RenderSync(Seed(Binding()));
        cut.Find("tr[data-key='A'] input.az-sync-check").Change(false);
        Assert.Equal(["B"], Workflow.Selections[^1]);
        Assert.Contains("1 of 2 changes", cut.Markup);
    }

    [Fact]
    public void Render_Conflict_BlocksOnlyItsKey()
    {
        Workflow.PushPlan = (b, e) => Plan(b, e, [Add("A", "1")], [Conflict("C")]);
        var cut = RenderSync(Seed(Binding()));
        Assert.Contains("Keep local", cut.Markup);
        Assert.DoesNotContain("C", Workflow.Selections[^1]);
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void KeepLocal_LocalFiles_ResolvesThroughTheWorkflowAndRePreviews()
    {
        Workflow.PushPlan = (b, e) => Plan(b, e, [Add("A", "1")], [Conflict("C")]);
        var cut = RenderSync(Seed(Binding()));
        Click(cut, "Keep local");
        Assert.Equal([("C", ConflictChoice.KeepLocal)], Workflow.Resolved);
        Assert.Empty(Drafts.Resolved);
        Assert.Equal(2, Workflow.PushPreviews.Count);
    }

    [Fact]
    public void KeepRemote_Draft_ResolvesThroughTheDraftService()
    {
        Workflow.PushPlan = (b, e) => Plan(b, e, [Add("A", "1")], [Conflict("C")]);
        var session = Seed(Binding(), view: SyncView.Draft);
        session.SetDraft("dev", Draft(Dev), 1);
        var cut = RenderSync(session);
        Click(cut, "Keep remote");
        Assert.Equal([("C", ConflictChoice.KeepRemote)], Drafts.Resolved);
        Assert.Empty(Workflow.Resolved);
        Assert.Equal("{\"resolved\":true}", session.DraftFor("dev")!.Json);
    }

    [Fact]
    public void Render_RequiresVaultChoice_ShowsPickerAndDisablesApply()
    {
        Workflow.PushPlan = VaultPlan;
        Workflow.Vaults = [Vault("kv-orders"), Vault("kv-shared")];
        var cut = RenderSync(Seed(Binding()));
        Assert.Contains("Choose vault", cut.Markup);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        cut.Find(".az-vault-picker input").Input("shared");
        Assert.Single(cut.FindAll(".az-vault-item"));
    }

    [Fact]
    public void ChooseVault_SetsVaultUpdatesBindingAndRePreviews()
    {
        Workflow.PushPlan = VaultPlan;
        Workflow.Vaults = [Vault("kv-orders")];
        var cut = RenderSync(Seed(Binding()));
        cut.Find(".az-vault-item").Click();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Choose vault", cut.Markup));
        Assert.Equal([("dev", new Uri("https://kv-orders.vault.azure.net"))], WorkspaceService.Vaults);
        Assert.True(CurrentBinding!.VaultByEnvironment.ContainsKey("dev"));
        Assert.Equal(2, Workflow.PushPreviews.Count);
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Apply_Confirmed_AppliesSelectionAndRePreviews()
    {
        var providers = Providers;
        var cut = RenderSync(Seed(Binding()));
        Apply(cut).Click();
        Click(providers, "Apply");
        cut.WaitForAssertion(() => Assert.Single(Workflow.Applied));
        Assert.Equal(new ApplyConfirmation(null, false), Workflow.Applied[0].Confirmation);
        cut.WaitForAssertion(() => Assert.Equal(2, Workflow.PushPreviews.Count));
    }

    [Fact]
    public void Apply_ProtectedAndTypedConfirmationCancelled_DoesNotApply()
    {
        var providers = Providers;
        var cut = RenderSync(Seed(Binding("prod"), env: "prod"));
        Apply(cut).Click();
        Click(providers, "Apply");
        providers.WaitForAssertion(() => Assert.Contains("Type <strong>prod</strong> to confirm", providers.Markup));
        Click(providers, "Cancel");
        Assert.Empty(Workflow.Applied);
    }

    [Fact]
    public void Apply_ProtectedWithTypedName_PassesTheConfirmation()
    {
        var providers = Providers;
        var cut = RenderSync(Seed(Binding("prod"), env: "prod"));
        Apply(cut).Click();
        Click(providers, "Apply");
        providers.WaitForAssertion(() => providers.Find("input.az-input").Input("PROD"));
        Click(providers, "Confirm");
        cut.WaitForAssertion(() => Assert.Single(Workflow.Applied));
        Assert.Equal("prod", Workflow.Applied[0].Confirmation.TypedEnvironmentName);
    }

    [Fact]
    public void Apply_TouchesGlobal_NeedsTheCheckbox()
    {
        Workflow.PushPlan = (b, e) => Plan(b, e, [Add("Shared", "1", ConfigScope.Global)]);
        var providers = Providers;
        var cut = RenderSync(Seed(Binding()));
        Assert.Contains("touches global", cut.Markup);
        Apply(cut).Click();
        providers.WaitForAssertion(() => Assert.True(providers.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Apply").HasAttribute("disabled")));
        providers.Find(".az-apply input[type=checkbox]").Change(true);
        Click(providers, "Apply");
        cut.WaitForAssertion(() => Assert.Single(Workflow.Applied));
        Assert.True(Workflow.Applied[0].Confirmation.GlobalConfirmed);
    }

    [Fact]
    public void Apply_WhileRunning_DisablesTheActions()
    {
        Workflow.ApplyGate = new TaskCompletionSource();
        var providers = Providers;
        var cut = RenderSync(Seed(Binding()));
        Apply(cut).Click();
        Click(providers, "Apply");
        cut.WaitForAssertion(() => Assert.True(Apply(cut).HasAttribute("disabled")));
        Assert.All(cut.FindAll("input.az-sync-check"), c => Assert.True(c.HasAttribute("disabled")));
        cut.InvokeAsync(() => Workflow.ApplyGate.SetResult());
        cut.WaitForAssertion(() => Assert.False(Apply(cut).HasAttribute("disabled")));
    }

    [Fact]
    public void Apply_FailedKey_ShowsRefreshPlan()
    {
        Workflow.ApplyResult = new ApplyResult(1, ["A"]);
        var providers = Providers;
        var cut = RenderSync(Seed(Binding()));
        Apply(cut).Click();
        Click(providers, "Apply");
        cut.WaitForAssertion(() => Assert.Contains("1 change failed: A changed in Azure (412)", cut.Markup));
        Click(cut, "Refresh plan");
        cut.WaitForAssertion(() => Assert.DoesNotContain("Refresh plan", cut.Markup));
        Assert.Equal(2, Workflow.PushPreviews.Count);
    }

    [Fact]
    public void ApplyDraft_Succeeded_ClearsTheDraft()
    {
        var providers = Providers;
        var session = Seed(Binding(), env: "prod", view: SyncView.Draft);
        session.SetDraft("prod", Draft(Prod), 1);
        var cut = RenderSync(session);
        Assert.Contains("Apply edits to prod", cut.Markup);
        Apply(cut).Click();
        Click(providers, "Apply");
        providers.WaitForAssertion(() => providers.Find("input.az-input").Input("prod"));
        Click(providers, "Confirm");
        cut.WaitForAssertion(() => Assert.Null(session.DraftFor("prod")));
        Assert.Equal(0, session.TotalPending);
    }

    [Fact]
    public void Render_SecretChange_NeverShowsTheValue()
    {
        Workflow.PushPlan = (b, e) => VaultPlan(b with { VaultByEnvironment = new Dictionary<string, Uri> { ["dev"] = new("https://kv.vault.azure.net") } }, e);
        var cut = RenderSync(Seed(Binding()));
        cut.Find("tr[data-key='Api:Key']").Click();
        Assert.Contains("Secret value will be set", cut.Markup);
        Assert.DoesNotContain(Sentinel, cut.Markup);
    }

    private static AzureResource Vault(string name) =>
        new($"/kv/{name}", name, new Uri($"https://{name}.vault.azure.net"), "sub", "rg", new Dictionary<string, string>());
}
