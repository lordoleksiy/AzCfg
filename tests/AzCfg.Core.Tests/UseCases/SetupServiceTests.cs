using AzCfg.Core.Model;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public class SetupServiceTests
{
    private static AzureResource Store(string name, string? envTag = null) => new(
        $"/subscriptions/s/{name}", name, new Uri($"https://{name}.azconfig.io"), "s", "rg",
        envTag is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["environment"] = envTag });

    private static Task<IReadOnlyList<EnvironmentCandidate>> Discover(params AzureResource[] stores) =>
        new SetupService(new StubResourceDiscovery(stores), new ProfileRecordingStateStore(), new FakeConfigStore([])).DiscoverEnvironmentsAsync(CancellationToken.None);

    [Theory]
    [InlineData("app-dev", "dev", false)]
    [InlineData("app-DEV", "dev", false)]
    [InlineData("app-stg", "stage", false)]
    [InlineData("app-stage", "stage", false)]
    [InlineData("app-prod", "prod", true)]
    public async Task Discover_SuffixOnly_MapsEnvironment(string storeName, string expected, bool isProtected)
    {
        var candidate = Assert.Single(await Discover(Store(storeName)));

        Assert.Equal(expected, candidate.Name);
        Assert.Equal(isProtected, candidate.IsProtected);
    }

    [Theory]
    [InlineData("app-dev", "production", true)]
    [InlineData("app-prod", "dev", false)]
    [InlineData("app-x", "prd", true)]
    public async Task Discover_TagWinsOverSuffix(string storeName, string tag, bool isProtected)
    {
        var candidate = Assert.Single(await Discover(Store(storeName, tag)));

        Assert.Equal(tag, candidate.Name);
        Assert.Equal(isProtected, candidate.IsProtected);
    }

    [Theory]
    [InlineData("app-qa")]
    [InlineData("app-prod-eu")]
    public async Task Discover_NoTagAndUnknownSuffix_NameIsNullAndNotProtected(string storeName)
    {
        var store = Store(storeName);

        var candidate = Assert.Single(await Discover(store));

        Assert.Null(candidate.Name);
        Assert.False(candidate.IsProtected);
        Assert.Equal(store, candidate.Store);
    }

    [Fact]
    public async Task Discover_BlankTag_FallsBackToSuffix()
    {
        var candidate = Assert.Single(await Discover(Store("app-prod", " ")));

        Assert.Equal("prod", candidate.Name);
    }

    [Fact]
    public async Task Discover_SeveralStoresForOneEnvironment_ReturnsCandidatePerStore()
    {
        var a = Store("crm-dev");
        var b = Store("billing-dev");

        var result = await Discover(a, b);

        Assert.Equal(new[] { a, b }, result.Select(c => c.Store));
        Assert.All(result, c => Assert.Equal("dev", c.Name));
    }

    [Fact]
    public async Task SaveProfile_DelegatesToStateStore()
    {
        var state = new ProfileRecordingStateStore();
        var profile = new UserProfile([new("dev", new Uri("https://dev.azconfig.io"), null, false)]);

        await new SetupService(new StubResourceDiscovery(), state, new FakeConfigStore([])).SaveProfileAsync(profile, CancellationToken.None);

        Assert.Same(profile, state.Saved);
    }

    [Fact]
    public async Task ListLabels_DelegatesToConfigStore()
    {
        var store = new FakeConfigStore([]) { Labels = ["Development", "Production"] };

        var labels = await new SetupService(new StubResourceDiscovery(), new ProfileRecordingStateStore(), store)
            .ListLabelsAsync(new Uri("https://shared.azconfig.io"), CancellationToken.None);

        Assert.Equal(["Development", "Production"], labels);
    }
}
