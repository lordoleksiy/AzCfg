using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Features.Environments;
using AzCfg.Ui.Tests.Features.Workspace;
using Bunit;

namespace AzCfg.Ui.Tests.Features.Environments;

public sealed class AccountStatusItemTests : ShellContext
{
    [Fact]
    public void Render_Connected_ShowsNothing() =>
        Assert.Empty(Render<AccountStatusItem>().Markup.Trim());

    [Theory]
    [InlineData(ConnectionState.AuthRequired, "Sign-in required")]
    [InlineData(ConnectionState.Forbidden, "No access")]
    [InlineData(ConnectionState.Unreachable, "Azure unreachable")]
    public void Render_Problem_ShowsRetry(ConnectionState state, string text)
    {
        Status.SetAccount(state);
        Assert.Contains(text, Render<AccountStatusItem>().Markup);
    }

    [Fact]
    public void Click_Retry_ChecksTheAccountAgain()
    {
        Status.SetAccount(ConnectionState.AuthRequired);
        var cut = Render<AccountStatusItem>();
        cut.Find("button.az-account-retry").Click();
        Assert.Contains(("Check Azure account", OperationKind.Read), Runner.Runs);
        Assert.Equal(1, Status.AccountChecks);
    }

    [Fact]
    public void StatusChange_RerendersTheItem()
    {
        var cut = Render<AccountStatusItem>();
        cut.InvokeAsync(() => Status.SetAccount(ConnectionState.AuthRequired));
        cut.WaitForAssertion(() => Assert.Contains("Sign-in required", cut.Markup));
        cut.InvokeAsync(() => Status.SetAccount(ConnectionState.Connected));
        cut.WaitForAssertion(() => Assert.Empty(cut.Markup.Trim()));
    }
}
