using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Infrastructure;
using AzCfg.Infrastructure.InMemory;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Tests.Application.Workspace.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Application.Workspace;

/// <summary>Real Core services over the in-memory Azure and a temp state folder; a fixed workspace scan.</summary>
public sealed class WorkspaceTestWorld : IDisposable
{
    public WorkspaceTestWorld(IReadOnlyList<SolutionInfo>? solutions = null, IReadOnlyList<WorkspaceProject>? projects = null, string? root = null)
    {
        Root = root ?? NewRoot();
        Directory.CreateDirectory(Root);
        World = new InMemoryWorld { Detector = new FixedProjectDetector([.. (projects ?? []).Select(p => p.Project)]) };
        var services = new ServiceCollection()
            .AddLogging()
            .AddAzCfg()
            .AddAzCfgInMemory(World)
            .AddSingleton<IWorkspaceScanner>(new FakeWorkspaceScanner(solutions ?? [], projects ?? []))
            .AddSingleton(new UiStatePaths(Path.Combine(Root, "state")))
            .AddWorkspaceServices()
            .AddEnvironmentServices();
        Provider = services.BuildServiceProvider();
    }

    public string Root { get; }

    public static string NewRoot() => Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"azcfg-ui-{Guid.NewGuid():N}"));

    public static WorkspaceProject ProjectAt(string root, string relativeFolder, string name)
    {
        var folder = Path.GetFullPath(Path.Combine(root, relativeFolder));
        return new WorkspaceProject(new ProjectInfo(folder, Path.Combine(folder, $"{name}.csproj"), name, false, null), []);
    }

    public InMemoryWorld World { get; }

    public ServiceProvider Provider { get; }

    public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    public string At(params string[] parts) => Path.GetFullPath(Path.Combine([Root, .. parts]));

    public LocalTargetBinding Target(WorkspaceProject project, LocalTargetKind kind = LocalTargetKind.UserSecrets) =>
        new(kind, project.Project.CsprojPath, LocalFileFormat.NestedJson);

    public ConfigEnvironment Env(string name, bool isProtected = false) => new(name, new Uri($"https://{name}.azconfig.io"), null, isProtected);

    public void Dispose()
    {
        Provider.Dispose();
        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
    }
}
