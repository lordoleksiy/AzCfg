using AzCfg.Core.Environments;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Components.Common;
using AzCfg.Ui.Tests.Support;
using Bunit;

namespace AzCfg.Ui.Tests.Components.Common;

public sealed class SmallComponentsTests : UiTestContext
{
    private static readonly EnvPillModel Prod = new("prod", EnvironmentKind.Production, true, true);
    private static readonly EnvPillModel Dev = new("dev", EnvironmentKind.Development, false, false);

    [Fact]
    public void EnvPill_Production_HasKindClassLockAndActiveDot()
    {
        var cut = Render<EnvPill>(p => p.Add(e => e.Env, Prod));
        Assert.Contains("kind-prod", cut.Find("button").ClassList);
        Assert.NotEmpty(cut.FindAll(".az-envpill-active"));
        Assert.Contains("(protected)", cut.Find("button").GetAttribute("title"));
        Assert.Contains("<svg", cut.Markup);
    }

    [Fact]
    public void EnvPill_Dev_HasNoLockAndNoActiveDot()
    {
        var cut = Render<EnvPill>(p => p.Add(e => e.Env, Dev));
        Assert.Contains("kind-dev", cut.Find("button").ClassList);
        Assert.Empty(cut.FindAll(".az-envpill-active"));
        Assert.DoesNotContain("<svg", cut.Markup);
    }

    [Theory]
    [InlineData(EnvironmentKind.Staging, "kind-stage")]
    [InlineData(EnvironmentKind.Custom, "kind-custom")]
    public void EnvPill_Kind_MapsToItsClass(EnvironmentKind kind, string expected)
    {
        var cut = Render<EnvPill>(p => p.Add(e => e.Env, new EnvPillModel("x", kind, false, false)));
        Assert.Contains(expected, cut.Find("button").ClassList);
    }

    [Fact]
    public void EnvironmentSwitcher_MarksSelectedAndReportsClick()
    {
        string? chosen = null;
        var cut = Render<EnvironmentSwitcher>(p => p
            .Add(s => s.Environments, [Dev, Prod])
            .Add(s => s.SelectedName, "PROD")
            .Add(s => s.OnSelect, (string name) => chosen = name));
        Assert.Equal(2, cut.FindAll("button").Count);
        Assert.Contains("selected", cut.FindAll("button")[1].ClassList);
        cut.FindAll("button")[0].Click();
        Assert.Equal("dev", chosen);
    }

    [Theory]
    [InlineData(ConnectionState.Connected, "ok")]
    [InlineData(ConnectionState.Checking, "busy")]
    [InlineData(ConnectionState.AuthRequired, "bad")]
    [InlineData(ConnectionState.Forbidden, "bad")]
    [InlineData(ConnectionState.Unreachable, "bad")]
    [InlineData(ConnectionState.Unknown, "unknown")]
    public void StatusDot_State_MapsToColourClass(ConnectionState state, string expected) =>
        Assert.Contains(expected, Render<StatusDot>(p => p.Add(d => d.State, state)).Find(".az-status-dot").ClassList);

    [Theory]
    [InlineData(DiffKind.LocalAdded, "A", "added")]
    [InlineData(DiffKind.LocalModified, "M", "modified")]
    [InlineData(DiffKind.LocalDeleted, "D", "deleted")]
    [InlineData(DiffKind.RemoteModified, "⇣", "remote")]
    [InlineData(DiffKind.Conflict, "!", "conflict")]
    public void DiffBadge_Kind_ShowsGlyphInItsColourClass(DiffKind kind, string glyph, string cssClass)
    {
        var badge = Render<DiffBadge>(p => p.Add(b => b.Kind, kind)).Find(".az-diff-badge");
        Assert.Equal(glyph, badge.TextContent);
        Assert.Contains(cssClass, badge.ClassList);
    }

    [Fact]
    public void DiffBadge_NullOrUnchangedWithoutDraft_RendersNothing()
    {
        Assert.Empty(Render<DiffBadge>().FindAll(".az-diff-badge"));
        Assert.Empty(Render<DiffBadge>(p => p.Add(b => b.Kind, DiffKind.Unchanged)).FindAll(".az-diff-badge"));
    }

    [Fact]
    public void DiffBadge_Draft_AddsDraftDot() =>
        Assert.Single(Render<DiffBadge>(p => p.Add(b => b.HasDraft, true)).FindAll(".az-diff-badge.draft"));

    [Fact]
    public void EmptyState_WithAction_ShowsOneLineAndButton()
    {
        var clicked = 0;
        var cut = Render<EmptyState>(p => p.Add(e => e.Text, "Nothing to push").Add(e => e.ActionText, "Refresh").Add(e => e.OnAction, () => clicked++));
        Assert.Contains("Nothing to push", cut.Markup);
        cut.Find("fluent-button").Click();
        Assert.Equal(1, clicked);
    }

    [Fact]
    public void EmptyState_WithoutAction_HasNoButton() =>
        Assert.Empty(Render<EmptyState>(p => p.Add(e => e.Text, "No differences")).FindAll("fluent-button"));

    [Fact]
    public void ErrorBar_WithRetry_ShowsMessageAndRaisesRetry()
    {
        var retried = 0;
        var cut = Render<ErrorBar>(p => p.Add(e => e.Message, "Azure is unreachable").Add(e => e.OnRetry, () => retried++));
        Assert.Contains("Azure is unreachable", cut.Markup);
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Retry").Click();
        Assert.Equal(1, retried);
    }

    [Fact]
    public void ErrorBar_WithDetails_CopiesThemToTheClipboard()
    {
        var cut = Render<ErrorBar>(p => p.Add(e => e.Message, "failed").Add(e => e.Details, "HttpRequestException: 503"));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Copy details").Click();
        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier == "navigator.clipboard.writeText");
        Assert.Equal("HttpRequestException: 503", call.Arguments[0]);
    }

    [Fact]
    public void ErrorBar_WithoutDetailsOrRetry_HasNoButtons() =>
        Assert.Empty(Render<ErrorBar>(p => p.Add(e => e.Message, "failed")).FindAll("fluent-button"));
}
