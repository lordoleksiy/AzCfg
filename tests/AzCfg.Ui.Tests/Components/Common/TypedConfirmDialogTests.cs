using AzCfg.Ui.Components.Common;
using AzCfg.Ui.Tests.Support;
using Bunit;
using Bunit.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Components.Common;

public sealed class TypedConfirmDialogTests : UiTestContext
{
    private IRenderedComponent<TypedConfirmDialog> RenderDialog(Action<bool>? onClose = null) =>
        Render<TypedConfirmDialog>(p =>
        {
            p.Add(d => d.Message, "Apply 3 changes to prod?").Add(d => d.Expected, "prod");
            if (onClose is not null)
            {
                p.Add(d => d.OnClose, onClose);
            }
        });

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<TypedConfirmDialog> cut) =>
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Confirm");

    [Fact]
    public void Render_NothingTyped_ConfirmIsDisabled() =>
        Assert.True(Confirm(RenderDialog()).HasAttribute("disabled"));

    [Fact]
    public void Input_WrongText_KeepsConfirmDisabled()
    {
        var cut = RenderDialog();
        cut.Find("input").Input("pro");
        Assert.True(Confirm(cut).HasAttribute("disabled"));
    }

    [Theory]
    [InlineData("prod")]
    [InlineData("PROD")]
    [InlineData("  Prod ")]
    public void Input_CaseInsensitiveMatch_EnablesConfirm(string typed)
    {
        var cut = RenderDialog();
        cut.Find("input").Input(typed);
        Assert.False(Confirm(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Click_ConfirmAfterMatch_ReportsTrue()
    {
        bool? result = null;
        var cut = RenderDialog(r => result = r);
        cut.Find("input").Input("prod");
        Confirm(cut).Click();
        Assert.True(result);
    }

    [Fact]
    public void Click_ConfirmWhileDisabled_ReportsNothing()
    {
        bool? result = null;
        var cut = RenderDialog(r => result = r);
        Confirm(cut).Click();
        Assert.Null(result);
    }

    [Fact]
    public void KeyDown_EnterInInput_DoesNotConfirm()
    {
        bool? result = null;
        var cut = RenderDialog(r => result = r);
        cut.Find("input").Input("prod");
        Assert.Throws<MissingEventHandlerException>(() => cut.Find("input").KeyDown("Enter"));
        Assert.DoesNotContain("<form", cut.Markup);
        Assert.Null(result);
    }

    [Fact]
    public async Task ConfirmTypedAsync_TypedMatchAndConfirm_ReturnsTrue()
    {
        var providers = Render<FluentProviders>();
        var task = Services.GetRequiredService<IDialogService>().ConfirmTypedAsync("Apply to prod", "Apply 3 changes?", "prod");
        providers.WaitForAssertion(() => Assert.Contains("Apply 3 changes?", providers.Markup));
        providers.Find("input.az-input").Input("PROD");
        providers.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Confirm").Click();
        Assert.True(await task);
    }

    [Fact]
    public async Task ConfirmTypedAsync_Cancel_ReturnsFalse()
    {
        var providers = Render<FluentProviders>();
        var task = Services.GetRequiredService<IDialogService>().ConfirmTypedAsync("Apply to prod", "Apply 3 changes?", "prod");
        providers.WaitForAssertion(() => Assert.Contains("Apply 3 changes?", providers.Markup));
        providers.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Cancel").Click();
        Assert.False(await task);
    }

    [Fact]
    public async Task ConfirmAsync_PrimaryButton_ReturnsTrue()
    {
        var providers = Render<FluentProviders>();
        var task = Services.GetRequiredService<IDialogService>().ConfirmAsync("Delete", "Delete key?", "Delete");
        providers.WaitForAssertion(() => Assert.Contains("Delete key?", providers.Markup));
        providers.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Delete").Click();
        Assert.True(await task);
    }
}
