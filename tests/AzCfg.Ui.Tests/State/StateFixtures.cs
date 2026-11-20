using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.State;

internal static class StateFixtures
{
    public static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), null, false);

    public static ProjectBinding Binding(string service = "crm")
    {
        var target = new LocalTargetBinding(LocalTargetKind.UserSecrets, "C:/src/Crm/Crm.csproj", LocalFileFormat.NestedJson);
        return new ProjectBinding("C:/src/Crm", service, target, target, new Dictionary<string, Uri>());
    }

    public static ProjectNode Node(string name, ProjectBinding? binding = null) =>
        new(new WorkspaceProject(new ProjectInfo($"C:/src/{name}", $"C:/src/{name}/{name}.csproj", name, false, null), []), binding);

    public static ConfigurationView View() => new(Binding(), Dev, false, [], [], DateTimeOffset.UnixEpoch);

    public static ComparisonView Comparison() =>
        new(Binding(), new CompareSide.Local(), new CompareSide.Environment(Dev), [], 0, "{}", "{}", DateTimeOffset.UnixEpoch);

    public static RemoteDraft Draft() => new(Binding(), Dev, "{}", "{}", new Dictionary<DraftKey, DraftSecret>());
}
