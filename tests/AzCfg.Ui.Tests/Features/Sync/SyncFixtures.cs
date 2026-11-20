using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Features.Sync;

internal static class SyncFixtures
{
    public const string Sentinel = "AZCFG-TEST-SECRET-7f3a";
    public const string ProjectId = "C:/src/Orders/Orders.csproj";

    public static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    public static readonly ConfigEnvironment Prod = new("prod", new Uri("https://prod.azconfig.io"), null, true);

    public static ProjectBinding Binding(string? active = "dev")
    {
        var target = new LocalTargetBinding(LocalTargetKind.UserSecrets, ProjectId, LocalFileFormat.NestedJson);
        return new ProjectBinding("C:/src/Orders", "orders", target, target, new Dictionary<string, Uri>(), active);
    }

    public static ProjectNode Node(ProjectBinding? binding) =>
        new(new WorkspaceProject(new ProjectInfo("C:/src/Orders", ProjectId, "Orders", false, null), []), binding);

    public static ConfigEntry Entry(string key, string value, ConfigScope scope = ConfigScope.Service) => new(key, scope, new ConfigValue.Plain(value));

    public static Change Add(string key, string value, ConfigScope scope = ConfigScope.Service) => new(ChangeKind.Add, null, Entry(key, value, scope));

    public static Conflict Conflict(string key) =>
        new(key, Item(key, "base"), Item(key, "local"), Item(key, "remote"));

    public static SyncPlan Plan(ProjectBinding binding, ConfigEnvironment env, IReadOnlyList<Change>? changes = null, IReadOnlyList<Conflict>? conflicts = null) =>
        new(binding, env, changes ?? [Add("A", "1"), Add("B", "2")], conflicts ?? [], false, (changes ?? []).Any(c => c.After?.Scope == ConfigScope.Global));

    /// <summary>A plan with a new secret awaiting a vault while the binding has none for the environment.</summary>
    public static SyncPlan VaultPlan(ProjectBinding binding, ConfigEnvironment env)
    {
        var needsVault = !binding.VaultByEnvironment.ContainsKey(env.Name);
        Change secret = needsVault
            ? new(ChangeKind.CreateSecret, null, null, "orders--Api--Key", new SecretString(Sentinel))
            : new(ChangeKind.CreateSecret, null, new ConfigEntry("Api:Key", ConfigScope.Service, new ConfigValue.KeyVaultRef(new Uri("https://kv.vault.azure.net/secrets/orders--Api--Key"))), "orders--Api--Key", new SecretString(Sentinel));
        return new(binding, env, [Add("A", "1"), secret], [], needsVault, false);
    }

    public static RemoteDraft Draft(ConfigEnvironment env, ProjectBinding? binding = null) =>
        new(binding ?? Binding(), env, "{}", "{\"A\":\"1\"}", new Dictionary<DraftKey, DraftSecret>());

    public static PullPreview Pull(ProjectBinding binding, ConfigEnvironment env) => new(
        binding,
        env,
        [new PullRow("A", DiffKind.RemoteAdded, false, null, "1"), new PullRow("B", DiffKind.RemoteModified, false, "old", "new"), new PullRow("S", DiffKind.RemoteModified, true, null, null)],
        [new PullRow("C", DiffKind.Conflict, false, "local", "remote")],
        ["L"]);

    private static StateItem Item(string key, string value) => new(key, ConfigScope.Service, new StateValue.Plain(value));
}
