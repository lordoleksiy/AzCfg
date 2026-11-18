using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Hosting;
using AzCfg.Ui.Shared;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Composition;

public sealed class AzCfgUiAppTests
{
    private sealed class FakeBindingFlow : IBindingFlow
    {
        public Task<bool> BindAsync(string projectId) => Task.FromResult(true);
    }

    private static readonly string StateRoot = Path.Combine(Path.GetTempPath(), "azcfg-ui-tests");

    [Fact]
    public async Task Build_ValidatesContainerAndResolvesScopedState()
    {
        await using var app = AzCfgUiApp.Build(["--no-browser", "--state-root", StateRoot]);
        using var scope = app.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WorkbenchNavigator>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<TabsState>(), scope.ServiceProvider.GetRequiredService<TabsState>());
        Assert.IsType<NotAvailableActivationFlow>(scope.ServiceProvider.GetRequiredService<IActivationFlow>());
        Assert.Equal(StateRoot, app.Services.GetRequiredService<UiStatePaths>().Root);
    }

    [Fact]
    public async Task Build_ResolvingScopedStateFromRoot_Throws()
    {
        await using var app = AzCfgUiApp.Build(["--state-root", StateRoot]);
        Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<TabsState>());
    }

    [Fact]
    public async Task Build_BindsCommandLineOptions()
    {
        await using var app = AzCfgUiApp.Build(["--port", "5123", "--no-browser", "--demo", "--state-root", StateRoot]);
        var options = app.Services.GetRequiredService<UiHostOptions>();
        Assert.Equal((5123, true, "full", StateRoot), (options.Port, options.NoBrowser, options.Demo, options.StateRoot));
    }

    [Fact]
    public async Task Build_Defaults_NoDemoAndDynamicPort()
    {
        await using var app = AzCfgUiApp.Build([]);
        var options = app.Services.GetRequiredService<UiHostOptions>();
        Assert.Equal((0, false, null), (options.Port, options.NoBrowser, options.Demo));
    }

    [Fact]
    public async Task Build_Overrides_WinOverDefaults()
    {
        await using var app = AzCfgUiApp.Build(["--state-root", StateRoot], s => s.AddScoped<IBindingFlow, FakeBindingFlow>());
        using var scope = app.Services.CreateScope();
        Assert.IsType<FakeBindingFlow>(scope.ServiceProvider.GetRequiredService<IBindingFlow>());
    }

    [Theory]
    [InlineData("--demo=empty", "--demo=empty")]
    [InlineData("--demo", "--demo=full")]
    [InlineData("--no-browser", "--no-browser=true")]
    [InlineData("5000", "5000")]
    public void NormalizeArgs_BareSwitches_GetValues(string arg, string expected) =>
        Assert.Equal([expected], AzCfgUiApp.NormalizeArgs([arg]));

    [Fact]
    public async Task Get_Root_ServesShellWithLocalAssets()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var token = factory.Services.GetRequiredService<AccessToken>().Value;
        var html = await factory.CreateClient().GetStringAsync($"/login?t={token}");
        Assert.Contains("_framework/blazor.web.js", html);
        Assert.Contains("_content/BlazorMonaco/lib/monaco-editor/min/vs/loader.js", html);
        Assert.Contains("Microsoft.FluentUI.AspNetCore.Components.bundle.scp.css", html);
        Assert.DoesNotContain("https://", html);
    }
}
