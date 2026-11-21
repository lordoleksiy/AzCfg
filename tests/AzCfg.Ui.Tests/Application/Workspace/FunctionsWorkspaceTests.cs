using AzCfg.Core.Model;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Application.Workspace;

/// <summary>An Azure Functions project (<c>AzureFunctionsVersion</c>) with a <c>local.settings.json</c>: bindable as a values target.</summary>
public sealed class FunctionsWorkspaceTests : IDisposable
{
    private readonly WorkspaceProject _functions;
    private readonly WorkspaceTestWorld _w;

    public FunctionsWorkspaceTests()
    {
        var root = WorkspaceTestWorld.NewRoot();
        var folder = Path.GetFullPath(Path.Combine(root, "src", "Orders.Functions"));
        Directory.CreateDirectory(folder);
        var csproj = Path.Combine(folder, "Orders.Functions.csproj");
        File.WriteAllText(csproj, "<Project />");
        File.WriteAllText(Path.Combine(folder, "local.settings.json"), """{ "IsEncrypted": false, "Values": {} }""");
        _functions = new WorkspaceProject(new ProjectInfo(folder, csproj, "Orders.Functions", true, null), []);
        _w = new WorkspaceTestWorld(projects: [_functions], root: root);
        _w.World.State.SaveProfileAsync(new UserProfile([_w.Env("dev")]), default).GetAwaiter().GetResult();
    }

    public void Dispose() => _w.Dispose();

    private IWorkspaceService Service => _w.Get<IWorkspaceService>();

    [Fact]
    public async Task ProposeSetupAsync_FunctionsProject_OffersLocalSettingsAsFlatDictionaryValuesTarget()
    {
        var proposal = await Service.ProposeSetupAsync(new ProjectNode(_functions, null), default);

        var option = Assert.Single(proposal.Resolution.TargetOptions, o => o.Target.Kind == LocalTargetKind.LocalSettings);
        Assert.Equal(LocalFileFormat.FlatDictionary, option.Target.Format);
        Assert.True(option.Exists);
    }

    [Fact]
    public async Task BindAsync_FunctionsProject_BindsLocalSettingsValuesAndSavesIt()
    {
        var values = new LocalTargetBinding(LocalTargetKind.LocalSettings, _functions.Project.CsprojPath, LocalFileFormat.FlatDictionary);

        var binding = await Service.BindAsync(new ProjectNode(_functions, null), "orders", values, _w.Target(_functions), default);

        Assert.Equal(values, binding.ValuesTarget);
        Assert.Equal(values, _w.World.State.Binding(binding.RepoPath)!.ValuesTarget);
    }
}
