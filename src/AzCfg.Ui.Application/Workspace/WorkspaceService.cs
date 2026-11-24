using AzCfg.Core.Abstractions;
using AzCfg.Core.Environments;
using AzCfg.Core.Manifest;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Ui.Application.Workspace;

/// <summary>
/// <see cref="IWorkspaceService"/> over the Core services. A project's binding is keyed by the folder of its <c>.csproj</c>
/// (G4); re-binding goes through Core project settings (G5, ADR 0014); a bound project still gets target options and
/// service candidates for re-binding (G19). A shared <c>azcfg.json</c> naming a project (ADR 0012 amendment 2) wins: the project
/// opens bound from it through Core <see cref="IProjectService.ResolveAsync"/> (no wizard) and its node carries the manifest path.
/// </summary>
public sealed class WorkspaceService(
    IWorkspaceScanner scanner,
    IWorkspaceRegistry registry,
    BindingLocator locator,
    IProjectService projects,
    IProjectSettingsService settings,
    IUserStateStore state,
    IConfigStore store,
    IEnvironmentResolver resolver,
    IEnumerable<ILocalTarget> targets,
    IProjectManifestStore manifests) : IWorkspaceService
{
    public async Task<WorkspaceTree> OpenAsync(string rootPath, CancellationToken ct)
    {
        var scan = await scanner.ScanAsync(rootPath, ct);
        var nodes = new List<ProjectNode>();
        foreach (var project in scan.Projects)
        {
            nodes.Add(await NodeAsync(project, rootPath, ct));
        }

        var solutions = scan.Solutions
            .Select(s => new SolutionNode(s, [.. nodes.Where(n => s.ProjectPaths.Any(p => PathKeys.Same(p, n.Id)))]))
            .ToList();
        var loose = nodes.Where(n => !solutions.Any(s => s.Projects.Contains(n))).ToList();
        await registry.AddOrTouchAsync(rootPath, ct);
        return new WorkspaceTree(rootPath, solutions, loose);
    }

    public async Task<ProjectSetupProposal> ProposeSetupAsync(ProjectNode project, CancellationToken ct)
    {
        var info = project.Project.Project;
        if (project.Binding is not { } binding)
        {
            // With a manifest Core creates and saves the binding from it: the proposal then carries it as Existing (no wizard).
            var resolution = await projects.ResolveAsync(FolderOf(info), ct);
            return new ProjectSetupProposal(info, resolution with { TargetOptions = [.. resolution.TargetOptions.Where(o => PathKeys.Same(o.Target.ProjectPath, info.CsprojPath))] });
        }

        var profile = await state.LoadProfileAsync(ct);
        var environments = profile is null ? [] : resolver.Resolve(profile);
        var prefixes = EnvironmentRules.SelectDefault(environments, binding) is { } env
            ? await store.ListServicePrefixesAsync(env, ct).ToListAsync(ct)
            : [];
        IReadOnlyList<string> candidates = [binding.Service, .. prefixes.Where(p => !p.Equals(binding.Service, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)];
        var options = new List<LocalTargetOption>();
        foreach (var target in targets.Where(t => t.CanHandle(info)))
        {
            var format = target.Kind == LocalTargetKind.LocalSettings ? LocalFileFormat.FlatDictionary : LocalFileFormat.NestedJson;
            var option = await target.InspectAsync(new LocalTargetBinding(target.Kind, info.CsprojPath, format), ct);
            options.Add(profile?.GitIgnoreLocalFiles == true ? option with { IsSafeForSecrets = true } : option);
        }

        return new ProjectSetupProposal(info, new ProjectResolution(candidates, false, options, binding, project.ManifestPath));
    }

    /// <summary>
    /// First bind: Core <see cref="IProjectService.BindAsync"/>. Re-bind: Core project settings (ADR 0014) for each changed key, so the
    /// rules match the CLI: vaults and ignore stay; a new service clears the active environment and drops the snapshots.
    /// </summary>
    public async Task<ProjectBinding> BindAsync(ProjectNode project, string service, LocalTargetBinding values, LocalTargetBinding secrets, CancellationToken ct)
    {
        // A binding created meanwhile (e.g. from azcfg.json by ProposeSetupAsync) is re-bound, never overwritten.
        if ((project.Binding ?? await state.LoadBindingAsync(FolderOf(project.Project.Project), ct)) is not { } old)
        {
            return await projects.BindAsync(FolderOf(project.Project.Project), service, values, secrets, ct);
        }

        if (values != old.ValuesTarget)
        {
            await settings.SetAsync(old.RepoPath, ProjectSettingKeys.Values, ProjectSettingKeys.TargetValue(values), ct);
        }

        if (secrets != old.SecretsTarget)
        {
            await settings.SetAsync(old.RepoPath, ProjectSettingKeys.Secrets, ProjectSettingKeys.TargetValue(secrets), ct);
        }

        if (!string.Equals(service, old.Service, StringComparison.Ordinal))
        {
            await settings.SetAsync(old.RepoPath, ProjectSettingKeys.Service, service, ct);
        }

        return await Reload(old.RepoPath, ct);
    }

    /// <summary>Through Core <c>project.vault.&lt;env&gt;</c> (ADR 0014): same validation and env-name rules as the CLI.</summary>
    public async Task<ProjectBinding> SetVaultAsync(ProjectBinding binding, string environment, Uri? vault, CancellationToken ct)
    {
        await settings.SetAsync(binding.RepoPath, ProjectSettingKeys.VaultPrefix + environment, vault?.OriginalString, ct);
        return await Reload(binding.RepoPath, ct);
    }

    public Task<ProjectBinding> AddIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct) =>
        projects.AddIgnoreAsync(binding.RepoPath, pattern, ct);

    public Task<ProjectBinding> RemoveIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct) =>
        projects.RemoveIgnoreAsync(binding.RepoPath, pattern, ct);

    /// <summary>
    /// Core <c>project.shared = true</c>: writes the binding's shared fields to <c>azcfg.json</c> in the binding's folder; returns
    /// its path. Refuses when a manifest in effect already shares another project (it would be overwritten).
    /// </summary>
    public async Task<string> ShareAsync(ProjectNode project, CancellationToken ct)
    {
        var binding = project.Binding ?? throw new InvalidOperationException("Bind the project before sharing it.");
        if (await manifests.FindAsync(binding.RepoPath, ct) is { } existing && !Names(existing, project.Id))
        {
            throw new InvalidOperationException($"'{existing.FilePath}' already shares another project (service '{existing.Manifest.Service}').");
        }

        var setting = await settings.SetAsync(binding.RepoPath, ProjectSettingKeys.Shared, "true", ct);
        return setting.Source ?? Path.Combine(binding.RepoPath, IProjectManifestStore.FileName);
    }

    /// <summary>
    /// G4 lookup, then the manifest found from the binding (or the project folder): when it names this project, Core resolution
    /// applies it (an unbound project gets bound, keyed by the manifest folder). A manifest that cannot be applied is reported on
    /// the node, never thrown: one broken file must not fail the whole tree.
    /// </summary>
    private async Task<ProjectNode> NodeAsync(WorkspaceProject project, string rootPath, CancellationToken ct)
    {
        var info = project.Project;
        var binding = await locator.FindAsync(info.CsprojPath, rootPath, ct);
        if (info.IsLibrary)
        {
            return new ProjectNode(project, binding);
        }

        try
        {
            if (await manifests.FindAsync(binding?.RepoPath ?? FolderOf(info), ct) is not { } manifest || !Names(manifest, info.CsprojPath))
            {
                return new ProjectNode(project, binding);
            }

            var resolution = await projects.ResolveAsync(binding?.RepoPath ?? Path.GetDirectoryName(manifest.FilePath)!, ct);
            return new ProjectNode(project, resolution.Existing ?? binding, resolution.ManifestPath);
        }
        catch (Exception e) when (e is ManifestException or InvalidOperationException or IOException)
        {
            return new ProjectNode(project, binding, ManifestError: e.Message);
        }
    }

    /// <summary><c>true</c> when the manifest's values or secrets project is <paramref name="csprojPath"/> or its folder.</summary>
    private static bool Names(ManifestLocation manifest, string csprojPath)
    {
        var folder = Path.GetDirectoryName(PathKeys.Full(manifest.FilePath))!;
        var csproj = PathKeys.Full(csprojPath);
        return new[] { manifest.Manifest.Values.Project, manifest.Manifest.Secrets.Project }
            .Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p, folder)))
            .Any(p => PathKeys.Same(p, csproj) || PathKeys.Same(p, Path.GetDirectoryName(csproj)!));
    }

    private async Task<ProjectBinding> Reload(string repoPath, CancellationToken ct) =>
        await state.LoadBindingAsync(repoPath, ct) ?? throw new InvalidOperationException("The project binding disappeared.");

    private static string FolderOf(ProjectInfo project) => Path.GetDirectoryName(PathKeys.Full(project.CsprojPath))!;
}
