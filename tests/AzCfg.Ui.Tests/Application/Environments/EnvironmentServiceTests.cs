using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Tests.Application.Workspace;

namespace AzCfg.Ui.Tests.Application.Environments;

public sealed class EnvironmentServiceTests : IDisposable
{
    private readonly WorkspaceTestWorld _w = new();

    public void Dispose() => _w.Dispose();

    private IEnvironmentService Service => _w.Get<IEnvironmentService>();

    private static ConfigEnvironment Env(string name, string endpoint = "https://x.azconfig.io", string? label = null, bool isProtected = false) =>
        new(name, new Uri(endpoint), label, isProtected);

    [Fact]
    public async Task LoadAsync_NoProfile_NullProfileAndNoEnvironments()
    {
        var snapshot = await Service.LoadAsync(default);

        Assert.Null(snapshot.Profile);
        Assert.Empty(snapshot.Environments);
    }

    [Fact]
    public async Task SaveAsync_LabelModeProfile_RoundTripsUnchanged()
    {
        var profile = new UserProfile(
            [Env("dev", label: "dev"), Env("prod", "https://x.azconfig.io", "prod", true)],
            EnvironmentMode.LabelPerEnvironment,
            "shared",
            GitIgnoreLocalFiles: true);

        var snapshot = await Service.SaveAsync(profile, default);

        Assert.Equal(profile.Environments, _w.World.State.Profile!.Environments);
        Assert.Equal(EnvironmentMode.LabelPerEnvironment, _w.World.State.Profile.Mode);
        Assert.Equal("shared", _w.World.State.Profile.DefaultLabel);
        Assert.True(_w.World.State.Profile.GitIgnoreLocalFiles);
        Assert.Equal(profile.Environments, snapshot.Profile!.Environments);
        Assert.Equal(2, snapshot.Environments.Count);
    }

    [Fact]
    public async Task SaveAsync_StoreModeWithoutLabels_SavedProfileKeepsNullLabels()
    {
        var profile = new UserProfile([Env("dev")], EnvironmentMode.StorePerEnvironment, "def");

        var snapshot = await Service.SaveAsync(profile, default);

        Assert.Null(_w.World.State.Profile!.Environments[0].Label);
        Assert.Null(_w.World.State.Profile.Environments[0].WriteLabel);
        Assert.Equal("def", snapshot.Environments[0].WriteLabel);
    }

    [Fact]
    public async Task SaveAsync_DuplicateNameIgnoringCase_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service.SaveAsync(new UserProfile([Env("dev"), Env("DEV", "https://y.azconfig.io")]), default));

        Assert.Null(_w.World.State.Profile);
    }

    [Fact]
    public async Task SaveAsync_EmptyName_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Service.SaveAsync(new UserProfile([Env(" ")]), default));

    [Theory]
    [InlineData("http://x.azconfig.io")]
    [InlineData("ftp://x.azconfig.io")]
    public async Task SaveAsync_NonHttpsEndpoint_Throws(string endpoint) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Service.SaveAsync(new UserProfile([Env("dev", endpoint)]), default));

    [Fact]
    public async Task SaveAsync_RelativeEndpoint_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service.SaveAsync(new UserProfile([new ConfigEnvironment("dev", new Uri("/rel", UriKind.Relative), null, false)]), default));

    [Fact]
    public async Task DiscoverAsync_ReturnsStoresFromDiscovery()
    {
        _w.World.Discovery.AddStore("crm-dev", "https://crm-dev.azconfig.io");

        var candidate = Assert.Single(await Service.DiscoverAsync(default));

        Assert.Equal("dev", candidate.Name);
    }

    [Fact]
    public async Task ListLabelsAsync_ReturnsStoreLabels()
    {
        _w.World.AppConfig.Set("https://x.azconfig.io", "crm:A", "1", "blue");

        Assert.Equal(["blue"], await Service.ListLabelsAsync(new Uri("https://x.azconfig.io"), default));
    }
}
