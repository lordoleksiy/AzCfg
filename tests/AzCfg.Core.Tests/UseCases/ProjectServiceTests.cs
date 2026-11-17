using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Core.Tests.Fakes;
using AzCfg.Core.UseCases;

namespace AzCfg.Core.Tests.UseCases;

public sealed class ProjectServiceTests
{
    private const string Repo = "/repo";

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly LocalTargetBinding Secrets = new(LocalTargetKind.UserSecrets, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly LocalTargetBinding Values = new(LocalTargetKind.AppSettingsLocal, "/repo/Orders/Orders.csproj", LocalFileFormat.NestedJson);

    private static ConfigEnvironment Env(string name) => new(name, new Uri($"https://{name}.azconfig.io"), null, false);

    private static ProjectInfo Project(string folder, string name) =>
        new(Repo, $"{Repo}/{folder}/{name}.csproj", name, false, null);

    private static ProjectService Create(
        FakeUserStateStore state, ProjectInfo[] projects, string[] devPrefixes, params FakeLocalTarget[] targets)
    {
        state.Profile ??= new UserProfile([Env("stage"), Env("dev")]);
        var store = new FakeConfigStore(new() { ["dev"] = devPrefixes, ["stage"] = ["other"] });
        return new(state, new FakeProjectDetector(projects), store, new SeparateStoresResolver(), targets);
    }

    [Fact]
    public async Task ResolveAsync_ExistingBinding_ReturnsIt()
    {
        var binding = new ProjectBinding(Repo, "orders", Values, Secrets, new Dictionary<string, Uri>());
        var service = Create(new() { Binding = binding }, [], []);

        var result = await service.ResolveAsync(Repo, Ct);

        Assert.Same(binding, result.Existing);
        Assert.True(result.IsConfident);
    }

    [Theory]
    [InlineData("Orders.Api", "src/orders-api", new[] { "orders-api", "billing" }, new[] { "orders-api" }, true)]
    [InlineData("Orders.Api", "src/Orders", new[] { "ORDERS", "billing" }, new[] { "ORDERS" }, true)]
    [InlineData("Orders.Api", "src/x", new[] { "orders-api", "orders.api" }, new[] { "orders-api", "orders.api" }, false)]
    [InlineData("Orders.Api", "src/x", new[] { "billing", "crm" }, new[] { "billing", "crm" }, false)]
    public async Task ResolveAsync_MatchesPrefixesIgnoringCaseAndSeparators(
        string name, string folder, string[] prefixes, string[] candidates, bool confident)
    {
        var service = Create(new(), [Project(folder, name)], prefixes);

        var result = await service.ResolveAsync(Repo, Ct);

        Assert.Null(result.Existing);
        Assert.Equal(candidates, result.Candidates);
        Assert.Equal(confident, result.IsConfident);
    }

    [Fact]
    public async Task ResolveAsync_NoProfile_HasNoCandidates()
    {
        var service = new ProjectService(new FakeUserStateStore(), new FakeProjectDetector(), new FakeConfigStore([]), new SeparateStoresResolver(), []);

        var result = await service.ResolveAsync(Repo, Ct);

        Assert.Empty(result.Candidates);
        Assert.False(result.IsConfident);
    }

    [Fact]
    public async Task ResolveAsync_OffersTargetsOnlyForProjectsTheyCanHandle()
    {
        var service = Create(
            new(),
            [Project("Orders", "Orders"), Project("Crm", "Crm")],
            [],
            new FakeLocalTarget(LocalTargetKind.AppSettingsLocal),
            new FakeLocalTarget(LocalTargetKind.LocalSettings, canHandle: false));

        var result = await service.ResolveAsync(Repo, Ct);

        Assert.Equal(2, result.TargetOptions.Count);
        Assert.All(result.TargetOptions, o => Assert.Equal(LocalTargetKind.AppSettingsLocal, o.Target.Kind));
        Assert.All(result.TargetOptions, o => Assert.Equal(LocalFileFormat.NestedJson, o.Target.Format));
    }

    [Fact]
    public async Task ResolveAsync_LocalSettingsTarget_UsesFlatDictionary()
    {
        var service = Create(new(), [Project("Orders", "Orders")], [], new FakeLocalTarget(LocalTargetKind.LocalSettings));

        var result = await service.ResolveAsync(Repo, Ct);

        Assert.Equal(LocalFileFormat.FlatDictionary, Assert.Single(result.TargetOptions).Target.Format);
    }

    [Fact]
    public async Task BindAsync_SafeSecretsTarget_SavesAndReturnsBinding()
    {
        var state = new FakeUserStateStore();
        var service = Create(state, [], [], new FakeLocalTarget(LocalTargetKind.UserSecrets));

        var binding = await service.BindAsync(Repo, "orders", Values, Secrets, Ct);

        Assert.Same(binding, state.Binding);
        Assert.Equal(("orders", Repo, Values, Secrets), (binding.Service, binding.RepoPath, binding.ValuesTarget, binding.SecretsTarget));
        Assert.Empty(binding.VaultByEnvironment);
    }

    [Fact]
    public async Task BindAsync_UnsafeSecretsTarget_ThrowsAndSavesNothing()
    {
        var state = new FakeUserStateStore();
        var service = Create(state, [], [], new FakeLocalTarget(LocalTargetKind.AppSettingsLocal, isSafeForSecrets: false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BindAsync(Repo, "orders", Values, Values, Ct));

        Assert.Null(state.Binding);
    }

    private static ProjectBinding Bound(params string[]? ignore) => new(Repo, "orders", Values, Secrets, new Dictionary<string, Uri>(), "dev", ignore);

    [Fact]
    public async Task AddIgnoreAsync_AddsNormalizedPatternAndSavesWithoutTouchingFiles()
    {
        var binding = Bound(null);
        var state = new FakeUserStateStore { Binding = binding };
        var target = new FakeLocalTarget(LocalTargetKind.UserSecrets);
        var service = Create(state, [], [], target);

        var result = await service.AddIgnoreAsync(Repo, "  Logging:*  ", Ct);

        Assert.Equal(["Logging:*"], result.Ignore!);
        Assert.Equal(binding with { Ignore = result.Ignore }, result);
        Assert.Same(result, state.Binding);
        Assert.Equal(0, target.Reads);
        Assert.Empty(target.Writes);
    }

    [Fact]
    public async Task AddIgnoreAsync_AppendsAndSkipsDuplicatesIgnoringCase()
    {
        var state = new FakeUserStateStore { Binding = Bound("Logging:*") };
        var service = Create(state, [], []);

        await service.AddIgnoreAsync(Repo, "global:*", Ct);
        var result = await service.AddIgnoreAsync(Repo, "LOGGING:*", Ct);

        Assert.Equal(["Logging:*", "global:*"], result.Ignore!);
        Assert.Same(result, state.Binding);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AddIgnoreAsync_BlankPattern_ThrowsAndSavesNothing(string pattern)
    {
        var binding = Bound(null);
        var state = new FakeUserStateStore { Binding = binding };

        await Assert.ThrowsAsync<ArgumentException>(() => Create(state, [], []).AddIgnoreAsync(Repo, pattern, Ct));

        Assert.Same(binding, state.Binding);
    }

    [Fact]
    public async Task RemoveIgnoreAsync_RemovesIgnoringCase()
    {
        var binding = Bound("Logging:*", "global:*");
        var state = new FakeUserStateStore { Binding = binding };

        var result = await Create(state, [], []).RemoveIgnoreAsync(Repo, " LOGGING:* ", Ct);

        Assert.Equal(["global:*"], result.Ignore!);
        Assert.Equal(binding with { Ignore = result.Ignore }, result);
        Assert.Same(result, state.Binding);
    }

    [Theory]
    [InlineData("B")]
    [InlineData(null)]
    public async Task RemoveIgnoreAsync_MissingPattern_IsNoOp(string? existing)
    {
        var state = new FakeUserStateStore { Binding = existing is null ? Bound(null) : Bound(existing) };

        var result = await Create(state, [], []).RemoveIgnoreAsync(Repo, "A", Ct);

        string[] expected = existing is null ? [] : [existing];
        Assert.Equal(expected, result.Ignore ?? []);
    }

    [Fact]
    public async Task IgnoreAsync_NotBound_Throws()
    {
        var service = Create(new FakeUserStateStore(), [], []);

        var add = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddIgnoreAsync(Repo, "A", Ct));
        var remove = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveIgnoreAsync(Repo, "A", Ct));

        Assert.Equal("This project is not bound yet. Run 'azcfg pull' first.", add.Message);
        Assert.Equal(add.Message, remove.Message);
    }
}
