using System.Xml;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Environments;
using AzCfg.Core.Manifest;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Ui.Application.Workspace;

/// <summary>
/// <see cref="IWorkspaceService"/> over the Core services. A project's binding is keyed by the folder of its <c>.csproj</c>
/// (G4); re-binding goes through Core project settings (G5, ADR 0014); a bound project still gets target options and
/// service candidates for re-binding (G19). A shared <c>azcfg.json</c> (ADR 0012 amendment 2) names a project when its values or
/// secrets project is that <c>.csproj</c>, or its folder holding exactly one <c>.csproj</c> (the Core lookup rule). Opening is a Read:
/// only an unbound project named by a manifest gets bound (Core <see cref="IProjectService.ResolveAsync"/>, keyed by the manifest
/// folder, no wizard); a bound project that differs from its manifest is only marked (<see cref="ProjectNode.ManifestChange"/>) until
/// <see cref="ApplyManifestAsync"/>. Per-project failures (an unreadable manifest or <c>.csproj</c>) are reported on that project's
/// node and never fail the whole tree.
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
    IProjectManifestStore manifests,
    IProjectDetector detector) : IWorkspaceService
{
    public async Task<WorkspaceTree> OpenAsync(string rootPath, CancellationToken ct)
    {
        var scan = await scanner.ScanAsync(rootPath, ct);
        var nodes = new List<ProjectNode>();
        foreach (var project in scan.Projects)
        {
            nodes.Add(await NodeAsync(project, scan.Projects, rootPath, ct));
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
            // A manifest naming the project: Core creates and saves the binding from it (keyed like OpenAsync does); the proposal
            // carries it as Existing, so the client skips the wizard.
            var from = await ManifestOfAsync(FolderOf(info), info.CsprojPath, ct) is { } manifest ? ManifestFolder(manifest) : FolderOf(info);
            var resolution = await projects.ResolveAsync(from, ct);
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
        // A binding created meanwhile from azcfg.json (ProposeSetupAsync, OpenAsync) is re-bound, never overwritten.
        if ((project.Binding ?? await SavedFromManifestAsync(project.Project.Project, ct)) is not { } old)
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
    /// The file sharing writes, as Core decides it (<c>project.shared</c>, which ignores another project's manifest): the manifest
    /// already describing this project, else <c>azcfg.json</c> in the binding's folder. Throws when the project is unbound.
    /// </summary>
    public async Task<string> PlanShareAsync(ProjectNode project, CancellationToken ct)
    {
        var binding = project.Binding ?? throw new InvalidOperationException("Bind the project before sharing it.");
        var shared = await settings.GetAsync(binding.RepoPath, ProjectSettingKeys.Shared, ct);
        return shared is { Value: "true", Source: { } source } ? source : Path.Combine(binding.RepoPath, IProjectManifestStore.FileName);
    }

    /// <summary>Core <c>project.shared = true</c>; returns the manifest path it wrote (or that already shares the project).</summary>
    public async Task<string> ShareAsync(ProjectNode project, CancellationToken ct)
    {
        var binding = project.Binding ?? throw new InvalidOperationException("Bind the project before sharing it.");
        var setting = await settings.SetAsync(binding.RepoPath, ProjectSettingKeys.Shared, "true", ct);
        return setting.Source ?? Path.Combine(binding.RepoPath, IProjectManifestStore.FileName);
    }

    /// <summary>
    /// Takes the manifest's service, targets and vaults into the bound project (Core <see cref="IProjectService.ResolveAsync"/>, a
    /// Write): a new service clears the active environment and drops the snapshots (<see cref="ProjectResolution.PreviousService"/>).
    /// </summary>
    public Task<ProjectResolution> ApplyManifestAsync(ProjectNode project, CancellationToken ct) =>
        projects.ResolveAsync((project.Binding ?? throw new InvalidOperationException("The project is not bound.")).RepoPath, ct);

    /// <summary>
    /// G4 lookup, then the manifest found from the binding (or the project folder) when it names this project: an unbound project
    /// is bound from it by Core; a bound one is compared read-only. A manifest that cannot be read is reported only on the project it
    /// belongs to (its folder is the project folder, or the project is the only one under it); for others it is as if absent.
    /// </summary>
    private async Task<ProjectNode> NodeAsync(WorkspaceProject project, IReadOnlyList<WorkspaceProject> all, string rootPath, CancellationToken ct)
    {
        var info = project.Project;
        var binding = await locator.FindAsync(info.CsprojPath, rootPath, ct);
        if (info.IsLibrary)
        {
            return new ProjectNode(project, binding);
        }

        ManifestLocation? manifest;
        try
        {
            manifest = await ManifestOfAsync(binding?.RepoPath ?? FolderOf(info), info.CsprojPath, ct);
        }
        catch (ManifestException e)
        {
            return Owns(e.FilePath, info, all) ? new ProjectNode(project, binding, ManifestError: e.Message) : new ProjectNode(project, binding);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or XmlException)
        {
            // The manifest read, or detecting the projects of a folder it names (a malformed .csproj).
            return new ProjectNode(project, binding, ManifestError: e.Message);
        }

        if (manifest is null)
        {
            return new ProjectNode(project, binding);
        }

        if (binding is not null)
        {
            return new ProjectNode(project, binding, manifest.FilePath, ManifestChange: Change(binding, manifest));
        }

        try
        {
            // Core may git-ignore the targets once here (ProfileGitIgnoreLocalFiles), as the CLI does on the first pull.
            var resolution = await projects.ResolveAsync(ManifestFolder(manifest), ct);
            return new ProjectNode(project, resolution.Existing, resolution.ManifestPath);
        }
        catch (ManifestException e)
        {
            return new ProjectNode(project, null, ManifestError: e.Message);
        }
        catch (InvalidOperationException e)
        {
            // Core applying this manifest to a new binding: a target it names that this azcfg cannot use (or not safe for secrets).
            return new ProjectNode(project, null, ManifestError: $"'{manifest.FilePath}': {e.Message.Replace($"'{manifest.FilePath}': ", string.Empty, StringComparison.Ordinal)}");
        }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException)
        {
            // Core detecting the projects the manifest names (a malformed .csproj) or reading a target.
            return new ProjectNode(project, null, ManifestError: e.Message);
        }
    }

    /// <summary>The manifest found from <paramref name="from"/> when it names <paramref name="csprojPath"/>; otherwise <c>null</c>.</summary>
    private async Task<ManifestLocation?> ManifestOfAsync(string from, string csprojPath, CancellationToken ct) =>
        await manifests.FindAsync(from, ct) is { } manifest && await NamesAsync(manifest, csprojPath, ct) ? manifest : null;

    /// <summary>The binding saved for an unbound node meanwhile: keyed by the manifest folder (as OpenAsync binds), else the project folder.</summary>
    private async Task<ProjectBinding?> SavedFromManifestAsync(ProjectInfo info, CancellationToken ct) =>
        await ManifestOfAsync(FolderOf(info), info.CsprojPath, ct) is { } manifest && await state.LoadBindingAsync(ManifestFolder(manifest), ct) is { } shared
            ? shared
            : await state.LoadBindingAsync(FolderOf(info), ct);

    /// <summary>The Core <c>ProjectLookup</c> rule: a manifest project is the <c>.csproj</c>, or a folder with exactly one <c>.csproj</c>.</summary>
    private async Task<bool> NamesAsync(ManifestLocation manifest, string csprojPath, CancellationToken ct)
    {
        foreach (var shared in new[] { manifest.Manifest.Values.Project, manifest.Manifest.Secrets.Project }.Distinct(StringComparer.Ordinal))
        {
            if (await ResolvesToAsync(manifest, shared, csprojPath, ct))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> ResolvesToAsync(ManifestLocation manifest, string sharedProject, string csprojPath, CancellationToken ct)
    {
        var wanted = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sharedProject, ManifestFolder(manifest)));
        if (PathKeys.Same(wanted, csprojPath))
        {
            return true;
        }

        if (!PathKeys.Same(wanted, FolderOfPath(csprojPath)))
        {
            return false;
        }

        var inFolder = (await detector.DetectAsync(wanted, ct)).Where(p => PathKeys.Same(FolderOfPath(p.CsprojPath), wanted)).ToList();
        return inFolder is [var only] && PathKeys.Same(only.CsprojPath, csprojPath);
    }

    /// <summary>
    /// What applying the manifest would change, read-only: <c>null</c> when the binding already has its service, targets (Core
    /// <c>ManifestBinding.Matches</c> rule) and vaults (environment names case-insensitive, root URIs without the trailing slash).
    /// </summary>
    private static ManifestChange? Change(ProjectBinding binding, ManifestLocation location)
    {
        var manifest = location.Manifest;
        var same = string.Equals(manifest.Service, binding.Service, StringComparison.Ordinal)
            && TargetMatches(location, binding.ValuesTarget, manifest.Values)
            && TargetMatches(location, binding.SecretsTarget, manifest.Secrets)
            && (manifest.Vaults ?? new Dictionary<string, Uri>()).All(v => binding.VaultByEnvironment.Any(b =>
                b.Key.Equals(v.Key, StringComparison.OrdinalIgnoreCase) && SameVault(b.Value, v.Value)));
        return same ? null : new ManifestChange(binding.Service, manifest.Service);
    }

    /// <summary>Core <c>ManifestBinding.Matches</c>: same target kind, and the manifest project is the <c>.csproj</c> or its folder (no uniqueness).</summary>
    private static bool TargetMatches(ManifestLocation location, LocalTargetBinding target, ManifestTarget shared)
    {
        var wanted = Path.TrimEndingDirectorySeparator(Path.GetFullPath(shared.Project, ManifestFolder(location)));
        return string.Equals(KindName(target), shared.Target, StringComparison.Ordinal)
            && (PathKeys.Same(wanted, target.ProjectPath) || PathKeys.Same(wanted, FolderOfPath(target.ProjectPath)));
    }

    /// <summary>The <c>config project.*</c> target name (<c>user-secrets</c>, …): the part of <see cref="ProjectSettingKeys.TargetValue"/> before the path.</summary>
    private static string KindName(LocalTargetBinding target) => ProjectSettingKeys.TargetValue(target).Split(':', 2)[0];

    private static bool SameVault(Uri a, Uri b) =>
        string.Equals(a.OriginalString.Trim().TrimEnd('/'), b.OriginalString.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>A broken manifest belongs to a project when it sits in the project folder or the project is the only one under its folder.</summary>
    private static bool Owns(string? manifestPath, ProjectInfo info, IReadOnlyList<WorkspaceProject> all)
    {
        if (manifestPath is null)
        {
            return true;
        }

        var folder = FolderOfPath(manifestPath);
        if (PathKeys.Same(folder, FolderOf(info)))
        {
            return true;
        }

        var under = all.Where(p => IsUnder(FolderOf(p.Project), folder)).ToList();
        return under is [var only] && PathKeys.Same(only.Project.CsprojPath, info.CsprojPath);
    }

    private static bool IsUnder(string dir, string root)
    {
        var full = Path.TrimEndingDirectorySeparator(PathKeys.Full(dir));
        var top = Path.TrimEndingDirectorySeparator(PathKeys.Full(root));
        return PathKeys.Comparer.Equals(full, top) || full.StartsWith(top + Path.DirectorySeparatorChar, PathKeys.Comparison);
    }

    private async Task<ProjectBinding> Reload(string repoPath, CancellationToken ct) =>
        await state.LoadBindingAsync(repoPath, ct) ?? throw new InvalidOperationException("The project binding disappeared.");

    private static string ManifestFolder(ManifestLocation manifest) => FolderOfPath(manifest.FilePath);

    private static string FolderOfPath(string path) => Path.GetDirectoryName(PathKeys.Full(path))!;

    private static string FolderOf(ProjectInfo project) => FolderOfPath(project.CsprojPath);
}
