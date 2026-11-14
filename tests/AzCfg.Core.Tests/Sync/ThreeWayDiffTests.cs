using System.Text.Json;
using AzCfg.Core.Model;
using AzCfg.Core.Security;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Sync;

public class ThreeWayDiffTests
{
    private const string SecretText = "super-secret-value";
    private static readonly Uri VaultUri = new("https://vault1.vault.azure.net/secrets/crm");
    private static readonly HmacSecretFingerprinter Fingerprinter = new(Enumerable.Repeat((byte)1, 32).ToArray());

    [Theory]
    [InlineData("a", "a", "a", DiffKind.Unchanged)]
    [InlineData("a", "b", "a", DiffKind.LocalModified)]
    [InlineData(null, "b", null, DiffKind.LocalAdded)]
    [InlineData("a", null, "a", DiffKind.LocalDeleted)]
    [InlineData("a", "a", "b", DiffKind.RemoteModified)]
    [InlineData(null, null, "b", DiffKind.RemoteAdded)]
    [InlineData("a", "a", null, DiffKind.RemoteDeleted)]
    [InlineData("a", "b", "c", DiffKind.Conflict)]
    [InlineData("a", null, "c", DiffKind.Conflict)]
    [InlineData("a", "b", null, DiffKind.Conflict)]
    [InlineData(null, "b", "c", DiffKind.Conflict)]
    [InlineData("a", "x", "x", DiffKind.Unchanged)]
    [InlineData(null, "x", "x", DiffKind.Unchanged)]
    [InlineData("a", null, null, DiffKind.Unchanged)]
    public void Compute_ClassifiesKey(string? @base, string? local, string? remote, DiffKind expected)
    {
        var item = Assert.Single(Diff(State(Plain("K", @base)), State(Plain("K", local)), State(Plain("K", remote))).Items);

        Assert.Equal("K", item.Key);
        Assert.Equal(expected, item.Kind);
    }

    [Fact]
    public void Compute_ReturnsItemsOrderedByKeyOrdinal()
    {
        var all = State(Plain("b", "1"), Plain("B", "1"), Plain("a", "1"));

        var diff = Diff(all, all, all);

        Assert.Equal(["B", "a", "b"], diff.Items.Select(i => i.Key));
    }

    [Fact]
    public void Compute_LocalScope_TakenFromRemoteThenBaseElseService()
    {
        var @base = State(Plain("fromBase", "1", ConfigScope.Global));
        var remote = State(Plain("fromRemote", "1", ConfigScope.Global));
        var local = State(Plain("fromBase", "1"), Plain("fromRemote", "1"), Plain("brandNew", "1", ConfigScope.Global));

        var items = Diff(@base, local, remote).Items.ToDictionary(i => i.Key);

        Assert.Equal(ConfigScope.Global, items["fromBase"].Local!.Scope);
        Assert.Equal(ConfigScope.Global, items["fromRemote"].Local!.Scope);
        Assert.Equal(ConfigScope.Service, items["brandNew"].Local!.Scope);
    }

    [Fact]
    public void Compute_LocalScope_PrefersRemoteOverBase()
    {
        var @base = State(Plain("K", "1", ConfigScope.Global));
        var remote = State(Plain("K", "1", ConfigScope.Service));

        var item = Assert.Single(Diff(@base, State(Plain("K", "1")), remote).Items);

        Assert.Equal(ConfigScope.Service, item.Local!.Scope);
    }

    [Fact]
    public void Compute_LocalPlainForRemoteSecret_BecomesSecretWithRemoteUri()
    {
        var remote = State(SecretItem("K", SecretText));

        var item = Assert.Single(Diff(State(), State(Plain("K", SecretText)), remote).Items);

        Assert.Equal(DiffKind.Unchanged, item.Kind);
        Assert.Equal(remote.Items["K"].Value, item.Local!.Value);
    }

    [Fact]
    public void Compute_LocalPlainForBaseSecret_UsesBaseUri()
    {
        var @base = State(SecretItem("K", SecretText));

        var item = Assert.Single(Diff(@base, State(Plain("K", "new")), State()).Items);

        var local = Assert.IsType<StateValue.Secret>(item.Local!.Value);
        Assert.Equal(VaultUri, local.SecretUri);
        Assert.Equal(Fingerprinter.Fingerprint(new SecretString("new")), local.Fingerprint);
    }

    [Fact]
    public void Compute_SecretFingerprintsDecideChange()
    {
        var secret = State(SecretItem("K", SecretText));

        Assert.Equal(DiffKind.Unchanged, Assert.Single(Diff(secret, State(Plain("K", SecretText)), secret).Items).Kind);
        Assert.Equal(DiffKind.LocalModified, Assert.Single(Diff(secret, State(Plain("K", "other")), secret).Items).Kind);
    }

    [Fact]
    public void Compute_LocalPlainForUnknownKey_StaysPlain()
    {
        var item = Assert.Single(Diff(State(), State(Plain("K", "v")), State()).Items);

        Assert.Equal(new StateValue.Plain("v"), item.Local!.Value);
    }

    [Fact]
    public void DiffItemAndConflict_Json_HasFingerprintAndUriButNotSecretValue()
    {
        var secret = SecretItem("K", SecretText);
        var fingerprint = ((StateValue.Secret)secret.Value).Fingerprint;
        var item = Assert.Single(Diff(State(secret), State(Plain("K", "changed")), State(secret)).Items);

        foreach (var json in new[] { JsonSerializer.Serialize(item), JsonSerializer.Serialize(new Conflict("K", secret, secret, secret)) })
        {
            Assert.Contains(fingerprint, json);
            Assert.Contains(VaultUri.ToString(), json);
            Assert.DoesNotContain(SecretText, json);
        }
    }

    private static ConfigDiff Diff(ConfigState @base, ConfigState local, ConfigState remote) =>
        ThreeWayDiff.Compute(@base, local, remote, Fingerprinter);

    private static ConfigState State(params StateItem?[] items) =>
        new(items.OfType<StateItem>().ToDictionary(i => i.Key, StringComparer.Ordinal));

    private static StateItem? Plain(string key, string? value, ConfigScope scope = ConfigScope.Service) =>
        value is null ? null : new(key, scope, new StateValue.Plain(value));

    private static StateItem SecretItem(string key, string text) =>
        new(key, ConfigScope.Service, new StateValue.Secret(VaultUri, Fingerprinter.Fingerprint(new SecretString(text))));
}
