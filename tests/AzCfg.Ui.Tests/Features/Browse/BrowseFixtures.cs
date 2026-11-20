using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Features.Browse;

internal static class BrowseFixtures
{
    public const string Sentinel = "AZCFG-TEST-SECRET-7f3a";
    public const string ProjectId = "C:/src/Orders/Orders.csproj";

    public static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    public static readonly ConfigEnvironment Prod = new("prod", new Uri("https://prod.azconfig.io"), null, true);

    public static LocalTargetBinding Target { get; } = new(LocalTargetKind.UserSecrets, ProjectId, LocalFileFormat.NestedJson);

    public static ProjectBinding Binding(string? active = "dev", IReadOnlyList<string>? ignore = null) =>
        new("C:/src/Orders", "orders", Target, Target, new Dictionary<string, Uri>(), active, ignore);

    public static ProjectNode Node(ProjectBinding? binding) =>
        new(new WorkspaceProject(new ProjectInfo("C:/src/Orders", ProjectId, "Orders", false, null), []), binding);

    /// <summary>A plain key; <paramref name="local"/> null = not set locally, <paramref name="remote"/> null = not in Azure.</summary>
    public static ConfigRow Plain(string key, string? remote, string? local = null, DiffKind? status = DiffKind.Unchanged, ConfigScope scope = ConfigScope.Service) =>
        new(key, status, false, false,
            local is null ? null : new LocalCell(local, false, Target, scope),
            remote is null ? null : new RemoteCell(remote, false, null, null, null, scope, null, null));

    /// <summary>A secret key: a Key Vault reference in Azure and a masked local value.</summary>
    public static ConfigRow Secret(string key, bool withLocal = true) =>
        new(key, DiffKind.Unchanged, true, false,
            withLocal ? new LocalCell(null, true, Target, ConfigScope.Service) : null,
            new RemoteCell(null, true, new Uri($"https://kv.vault.azure.net/secrets/orders--{key.Replace(':', '-')}"), $"orders--{key.Replace(':', '-')}", "kv.vault.azure.net", ConfigScope.Service, null, null));

    public static ConfigRow Ignored(string key, string local) =>
        new(key, null, false, true, new LocalCell(local, false, Target, ConfigScope.Service), null);

    /// <summary>Edit JSON of the plain keys (<c>Db:Host</c> → nested), enough for the real draft service.</summary>
    public static string EditJson(params (string Key, string Value)[] keys)
    {
        var root = new System.Text.Json.Nodes.JsonObject();
        foreach (var (key, value) in keys)
        {
            var node = root;
            var parts = key.Split(':');
            foreach (var part in parts[..^1])
            {
                node = (System.Text.Json.Nodes.JsonObject)(node[part] ??= new System.Text.Json.Nodes.JsonObject());
            }

            node[parts[^1]] = value;
        }

        return root.ToJsonString();
    }
}
