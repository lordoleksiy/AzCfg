using AzCfg.Core.Model;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Features.Workspace;

internal static class ShellFixtures
{
    public const string Root = "C:/src/Shop";

    public static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    public static readonly ConfigEnvironment Stage = new("stage", new Uri("https://stage.azconfig.io"), null, false);
    public static readonly ConfigEnvironment Prod = new("prod", new Uri("https://prod.azconfig.io"), null, true);

    public static string IdOf(string name) => $"{Root}/{name}/{name}.csproj";

    public static LocalTargetBinding Target(string name, LocalTargetKind kind = LocalTargetKind.UserSecrets) =>
        new(kind, IdOf(name), kind == LocalTargetKind.LocalSettings ? LocalFileFormat.FlatDictionary : LocalFileFormat.NestedJson);

    public static ProjectBinding Binding(string name, string service = "orders", string? active = "dev", LocalTargetKind values = LocalTargetKind.AppSettingsLocal, LocalTargetKind secrets = LocalTargetKind.UserSecrets) =>
        new($"{Root}/{name}", service, Target(name, values), Target(name, secrets), new Dictionary<string, Uri>(), active);

    public static ConfigFileInfo File(string name, ConfigFileKind kind, bool exists = true) =>
        new($"{Root}/{name}/{kind switch
        {
            ConfigFileKind.AppSettings => "appsettings.json",
            ConfigFileKind.AppSettingsLocal => "appsettings.local.json",
            ConfigFileKind.LocalSettings => "local.settings.json",
            ConfigFileKind.UserSecrets => "secrets.json",
            _ => "appsettings.Development.json",
        }}", kind, null, exists);

    public static ProjectNode Node(string name, ProjectBinding? binding = null, bool library = false, params ConfigFileInfo[] files) =>
        new(new WorkspaceProject(new ProjectInfo($"{Root}/{name}", IdOf(name), name, false, null, library), files), binding);

    public static SolutionNode Solution(string name, params ProjectNode[] projects) =>
        new(new SolutionInfo($"{Root}/{name}.slnx", name, SolutionFormat.Slnx, [.. projects.Select(p => p.Id)]), projects);

    public static WorkspaceTree Tree(IReadOnlyList<SolutionNode> solutions, params ProjectNode[] loose) => new(Root, solutions, loose);

    public static UserProfile Profile(params ConfigEnvironment[] environments) => new(environments);
}
