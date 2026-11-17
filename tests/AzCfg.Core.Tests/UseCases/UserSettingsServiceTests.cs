using AzCfg.Core.Model;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public class UserSettingsServiceTests
{
    private static readonly UserProfile Profile = new(
        [new("dev", new Uri("https://dev.azconfig.io"), "Development", false), new("prod", new Uri("https://prod.azconfig.io"), null, true)],
        EnvironmentMode.StorePerEnvironment,
        "Shared");

    private static (UserSettingsService Service, FakeUserStateStore State) Create(UserProfile? profile = null)
    {
        var state = new FakeUserStateStore { Profile = profile ?? Profile };
        return (new UserSettingsService(state), state);
    }

    [Fact]
    public async Task List_ReturnsModeDefaultLabelThenEnvironmentsInProfileOrder()
    {
        var (service, _) = Create();

        var settings = await service.ListAsync(CancellationToken.None);

        Assert.Equal(
            [
                new UserSetting("mode", "stores"),
                new UserSetting("defaultLabel", "Shared"),
                new UserSetting("env.dev.endpoint", "https://dev.azconfig.io"),
                new UserSetting("env.dev.label", "Development"),
                new UserSetting("env.dev.protected", "false"),
                new UserSetting("env.prod.endpoint", "https://prod.azconfig.io"),
                new UserSetting("env.prod.label", null),
                new UserSetting("env.prod.protected", "true"),
            ],
            settings);
    }

    [Fact]
    public async Task List_LabelMode_FormatsAsLabels()
    {
        var (service, _) = Create(new UserProfile([], EnvironmentMode.LabelPerEnvironment));

        var settings = await service.ListAsync(CancellationToken.None);

        Assert.Equal([new UserSetting("mode", "labels"), new UserSetting("defaultLabel", null)], settings);
    }

    [Theory]
    [InlineData("mode", "mode", "stores")]
    [InlineData("DefaultLabel", "defaultLabel", "Shared")]
    [InlineData("env.DEV.label", "env.dev.label", "Development")]
    [InlineData("env.prod.protected", "env.prod.protected", "true")]
    public async Task Get_MatchesKeyCaseInsensitively(string key, string expectedKey, string expectedValue)
    {
        var (service, _) = Create();

        var setting = await service.GetAsync(key, CancellationToken.None);

        Assert.Equal(new UserSetting(expectedKey, expectedValue), setting);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("env.qa.label")]
    [InlineData("env.dev.color")]
    public async Task Get_UnknownKey_ListsKnownKeys(string key)
    {
        var (service, _) = Create();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(key, CancellationToken.None));

        Assert.Contains(key, ex.Message);
        Assert.Contains("env.dev.endpoint", ex.Message);
        Assert.Contains("defaultLabel", ex.Message);
    }

    public static TheoryData<string, string, string?, Func<UserProfile, object?>, object?> ValidSets => new()
    {
        { "mode", "labels", "labels", p => p.Mode, EnvironmentMode.LabelPerEnvironment },
        { "MODE", "Stores", "stores", p => p.Mode, EnvironmentMode.StorePerEnvironment },
        { "defaultLabel", "Staging", "Staging", p => p.DefaultLabel, "Staging" },
        { "defaultLabel", "  ", null, p => p.DefaultLabel, null },
        { "env.dev.label", "Dev", "Dev", p => p.Environments[0].Label, "Dev" },
        { "env.Dev.label", "", null, p => p.Environments[0].Label, null },
        { "env.prod.endpoint", "https://other.azconfig.io", "https://other.azconfig.io", p => p.Environments[1].StoreEndpoint, new Uri("https://other.azconfig.io") },
        { "env.prod.protected", "False", "false", p => p.Environments[1].IsProtected, false },
        { "env.dev.protected", "true", "true", p => p.Environments[0].IsProtected, true },
    };

    [Theory]
    [MemberData(nameof(ValidSets))]
    public async Task Set_ValidValue_SavesProfileAndReturnsSetting(string key, string value, string? expectedValue, Func<UserProfile, object?> read, object? expected)
    {
        var (service, state) = Create();

        var setting = await service.SetAsync(key, value, CancellationToken.None);

        Assert.Equal(expectedValue, setting.Value);
        Assert.Equal(expected, read(state.Profile!));
    }

    [Fact]
    public async Task Set_ReturnsCanonicalKeyAndKeepsOtherSettings()
    {
        var (service, state) = Create();

        var setting = await service.SetAsync("ENV.PROD.LABEL", "Production", CancellationToken.None);

        Assert.Equal(new UserSetting("env.prod.label", "Production"), setting);
        Assert.Equal(Profile with { Environments = [Profile.Environments[0], Profile.Environments[1] with { Label = "Production" }] }, state.Profile, ProfileComparer);
    }

    [Theory]
    [InlineData("mode", "both", "stores")]
    [InlineData("env.dev.endpoint", "not a url", "URL")]
    [InlineData("env.dev.endpoint", "ftp://dev.azconfig.io", "URL")]
    [InlineData("env.dev.endpoint", "/relative", "URL")]
    [InlineData("env.dev.protected", "maybe", "true")]
    [InlineData("env.qa.label", "QA", "dev, prod")]
    [InlineData("env..label", "QA", "Unknown")]
    [InlineData("color", "red", "env.dev.endpoint")]
    public async Task Set_InvalidKeyOrValue_ThrowsWithClearMessageAndDoesNotSave(string key, string value, string expected)
    {
        var (service, state) = Create();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(key, value, CancellationToken.None));

        Assert.Contains(expected, ex.Message);
        Assert.Same(Profile, state.Profile);
    }

    [Fact]
    public async Task NoProfile_PointsToInit()
    {
        var state = new FakeUserStateStore();
        var service = new UserSettingsService(state);

        var list = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(CancellationToken.None));
        var get = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("mode", CancellationToken.None));
        var set = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync("mode", "labels", CancellationToken.None));

        Assert.All([list, get, set], ex => Assert.Equal("No profile found. Run 'azcfg init' first.", ex.Message));
        Assert.Null(state.Profile);
    }

    private static readonly IEqualityComparer<UserProfile?> ProfileComparer = EqualityComparer<UserProfile?>.Create(
        (a, b) => a is not null && b is not null && a.Mode == b.Mode && a.DefaultLabel == b.DefaultLabel && a.Environments.SequenceEqual(b.Environments),
        p => 0);
}
