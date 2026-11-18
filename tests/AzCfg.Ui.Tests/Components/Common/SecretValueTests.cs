using AzCfg.Core.Model;
using AzCfg.Ui.Application.Security;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Components.Common;
using AzCfg.Ui.Tests.Support;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AzCfg.Ui.Tests.Components.Common;

public sealed class SecretValueTests : UiTestContext
{
    private const string Sentinel = "AZCFG-TEST-SECRET-7f3a";

    private readonly FakeTimeProvider _time = new();
    private int _reveals;

    public SecretValueTests()
    {
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddScoped<TabsState>();
    }

    private IRenderedComponent<SecretValue> RenderSecret() =>
        Render<SecretValue>(p => p.Add(s => s.OnReveal, _ =>
        {
            _reveals++;
            return Task.FromResult<SecretString?>(new SecretString(Sentinel));
        }));

    [Fact]
    public void Render_ByDefault_ShowsMaskAndNeverTheValue()
    {
        var cut = RenderSecret();
        Assert.Contains(SecretPolicy.Mask, cut.Markup);
        Assert.DoesNotContain(Sentinel, cut.Markup);
        Assert.Equal(0, _reveals);
    }

    [Fact]
    public void Render_WithoutRevealCallback_HasNoToggle()
    {
        var cut = Render<SecretValue>();
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Click_Reveal_ShowsValueThenHideRestoresMask()
    {
        var cut = RenderSecret();
        cut.Find("button").Click();
        Assert.Contains(Sentinel, cut.Markup);
        cut.Find("button").Click();
        Assert.DoesNotContain(Sentinel, cut.Markup);
        Assert.Contains(SecretPolicy.Mask, cut.Markup);
    }

    [Fact]
    public void Reveal_After30Seconds_HidesAgain()
    {
        var cut = RenderSecret();
        cut.Find("button").Click();
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.Contains(Sentinel, cut.Markup);
        _time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Sentinel, cut.Markup));
    }

    [Fact]
    public void Reveal_ActiveTabChanges_HidesTheValue()
    {
        var tabs = Services.GetRequiredService<TabsState>();
        tabs.Open(new TabDescriptor(TabKind.Welcome), "Welcome");
        var cut = RenderSecret();
        cut.Find("button").Click();
        Assert.Contains(Sentinel, cut.Markup);
        cut.InvokeAsync(() => tabs.Open(new TabDescriptor(TabKind.Settings), "Settings"));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Sentinel, cut.Markup));
    }

    [Fact]
    public void Dispose_AfterReveal_ClearsTheValueAndStopsReacting()
    {
        var tabs = Services.GetRequiredService<TabsState>();
        var cut = RenderSecret();
        cut.Find("button").Click();
        var field = typeof(SecretValue).GetField("_revealed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.NotNull(field.GetValue(cut.Instance));
        cut.Instance.Dispose();
        Assert.Null(field.GetValue(cut.Instance));
        _time.Advance(TimeSpan.FromMinutes(1));
        tabs.Open(new TabDescriptor(TabKind.Welcome), "Welcome");
        Assert.Equal(1, _reveals);
    }

    [Fact]
    public void Render_RevealReturnsNull_KeepsMask()
    {
        var cut = Render<SecretValue>(p => p.Add(s => s.OnReveal, _ => Task.FromResult<SecretString?>(null)));
        cut.Find("button").Click();
        Assert.Contains(SecretPolicy.Mask, cut.Markup);
    }

    [Fact]
    public void ToString_OfRevealedSecret_NeverContainsTheValue() =>
        Assert.DoesNotContain(Sentinel, new SecretString(Sentinel).ToString());
}
