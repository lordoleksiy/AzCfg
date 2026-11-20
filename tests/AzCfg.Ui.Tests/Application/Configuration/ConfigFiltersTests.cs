using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;

namespace AzCfg.Ui.Tests.Application.Configuration;

public sealed class ConfigFiltersTests
{
    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "C:/src/Crm/Crm.csproj", LocalFileFormat.NestedJson);

    private static ConfigRow Plain(string key, string? local, string? remote, DiffKind? status = DiffKind.Unchanged, ConfigScope scope = ConfigScope.Service, bool ignored = false) =>
        new(key, status, false, ignored,
            local is null ? null : new LocalCell(local, false, Target, scope),
            remote is null ? null : new RemoteCell(remote, false, null, null, null, scope, null, null));

    private static ConfigRow Secret(string key) =>
        new(key, DiffKind.Unchanged, true, false, new LocalCell(null, true, Target, ConfigScope.Service),
            new RemoteCell(null, true, new Uri("https://kv.vault.azure.net/secrets/s"), "s", "kv.vault.azure.net", ConfigScope.Service, null, null));

    private static readonly IReadOnlyList<ConfigRow> Rows =
    [
        Plain("Db:Host", "alpha", "beta"),
        Plain("Log:Level", "Info", "Info", DiffKind.LocalModified),
        Plain("Shared:Flag", null, "on", null, ConfigScope.Global),
        Plain("Ign:Key", "x", null, null, ignored: true),
        Secret("Api:Key"),
    ];

    private static string[] Keys(ConfigFilter filter) => [.. ConfigFilters.Apply(Rows, filter).Select(r => r.Key)];

    [Fact]
    public void Apply_EmptyFilter_ReturnsAll() => Assert.Equal(5, Keys(new()).Length);

    [Theory]
    [InlineData("db:", "Db:Host")]
    [InlineData("ALPHA", "Db:Host")]
    [InlineData("beta", "Db:Host")]
    [InlineData("on", "Shared:Flag")]
    public void Apply_Text_MatchesKeysAndPlainValues(string text, string expected) =>
        Assert.Equal([expected], Keys(new(Text: text)).Where(k => k == expected));

    [Fact]
    public void Apply_Text_NeverMatchesSecretCells()
    {
        var row = new ConfigRow("Api:Key", null, true, false,
            new LocalCell("hidden-local", true, Target, ConfigScope.Service),
            new RemoteCell("hidden-remote", true, null, "s", null, ConfigScope.Service, null, null));

        Assert.Empty(ConfigFilters.Apply([row], new(Text: "hidden")));
    }

    [Fact]
    public void Apply_ChangedOnly_KeepsRowsWithNonUnchangedStatus() => Assert.Equal(["Log:Level"], Keys(new(ChangedOnly: true)));

    [Fact]
    public void Apply_SecretsOnly_KeepsSecretRows() => Assert.Equal(["Api:Key"], Keys(new(SecretsOnly: true)));

    [Fact]
    public void Apply_IgnoredOnly_KeepsIgnoredRows() => Assert.Equal(["Ign:Key"], Keys(new(IgnoredOnly: true)));

    [Fact]
    public void Apply_Scope_KeepsRowsOfThatScope() => Assert.Equal(["Shared:Flag"], Keys(new(Scope: ConfigScope.Global)));

    [Fact]
    public void Apply_CombinedFilters_AreAnded() => Assert.Empty(Keys(new(ChangedOnly: true, SecretsOnly: true)));
}
