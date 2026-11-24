using AzCfg.Core.Abstractions;
using AzCfg.Core.Manifest;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.UseCases;

/// <summary>
/// Resolves and binds projects. Service candidates are the prefixes of the <c>dev</c> environment store (the first
/// environment when none is named <c>dev</c>), matched to csproj/folder names ignoring case and treating <c>-</c> and
/// <c>.</c> as equal; exactly one match is confident. <see cref="BindAsync"/> throws
/// <see cref="InvalidOperationException"/> when the secrets target is not safe for secrets. <see cref="AddIgnoreAsync"/> and
/// <see cref="RemoveIgnoreAsync"/> only edit the saved binding's ignore patterns: they throw <see cref="ArgumentException"/>
/// for a blank pattern and <see cref="InvalidOperationException"/> when the project is not bound yet.
/// With <see cref="UserProfile.GitIgnoreLocalFiles"/> (ADR 0009) <see cref="ResolveAsync"/> reports every option as safe for secrets,
/// and <see cref="BindAsync"/> first asks the targets to git-ignore their files (it still throws when the target stays unsafe).
/// A shared <c>azcfg.json</c> (ADR 0012 amendment 2) found from the project path wins over the saved binding's shared fields:
/// without a binding one is created from it (no wizard); a binding with another service takes the manifest's service, clears the
/// active environment and drops the snapshots (the <c>config set project.service</c> rule); other targets or vaults are taken
/// as they are. The active environment, ignore patterns, snapshots and vaults of environments the manifest does not name stay per
/// user. A manifest that cannot be read or is invalid fails the resolution with its <see cref="ManifestException"/>: it is never
/// silently ignored, since the per-user fallback could write to other targets than the team's.
/// </summary>
public sealed class ProjectService(
    IUserStateStore state,
    IProjectDetector detector,
    IConfigStore store,
    IEnvironmentResolver resolver,
    IEnumerable<ILocalTarget> targets,
    IProjectManifestStore manifests) : IProjectService
{
    public async Task<ProjectResolution> ResolveAsync(string repoPath, CancellationToken ct)
    {
        var manifest = await manifests.FindAsync(repoPath, ct);
        var existing = await state.LoadBindingAsync(repoPath, ct);
        if (manifest is not null)
        {
            var binding = await ApplyManifestAsync(repoPath, manifest, existing, ct);
            var previous = existing is not null && existing.Service != binding.Service ? existing.Service : null;
            return new([], true, [], binding, manifest.FilePath, previous);
        }

        if (existing is not null)
        {
            return new([], true, [], existing);
        }

        var projects = await detector.DetectAsync(repoPath, ct);
        var profile = await state.LoadProfileAsync(ct);
        IReadOnlyList<ConfigEnvironment> environments = profile is null ? [] : resolver.Resolve(profile);
        var dev = environments.FirstOrDefault(e => e.Name.Equals("dev", StringComparison.OrdinalIgnoreCase))
            ?? environments.FirstOrDefault();
        var prefixes = dev is null ? [] : await store.ListServicePrefixesAsync(dev, ct).ToListAsync(ct);

        var names = projects
            .SelectMany(p => new[] { p.Name, Path.GetFileName(Path.GetDirectoryName(p.CsprojPath)) ?? p.Name })
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = prefixes.Where(p => names.Contains(Normalize(p))).ToList();

        var options = new List<LocalTargetOption>();
        foreach (var project in projects)
        {
            foreach (var target in targets.Where(t => t.CanHandle(project)))
            {
                var format = target.Kind == LocalTargetKind.LocalSettings ? LocalFileFormat.FlatDictionary : LocalFileFormat.NestedJson;
                var option = await target.InspectAsync(new(target.Kind, project.CsprojPath, format), ct);
                options.Add(profile?.GitIgnoreLocalFiles == true ? option with { IsSafeForSecrets = true } : option);
            }
        }

        return new(matches.Count > 0 ? matches : prefixes, matches.Count == 1, options, null);
    }

    public async Task<ProjectBinding> BindAsync(
        string repoPath,
        string service,
        LocalTargetBinding valuesTarget,
        LocalTargetBinding secretsTarget,
        CancellationToken ct)
    {
        await PrepareTargetsAsync(valuesTarget, secretsTarget, ct);
        var binding = new ProjectBinding(repoPath, service, valuesTarget, secretsTarget, new Dictionary<string, Uri>());
        await state.SaveBindingAsync(binding, ct);
        return binding;
    }

    /// <summary>In the gitignore mode git-ignores both targets' files; then rejects a secrets target that is not safe for secrets.</summary>
    private async Task PrepareTargetsAsync(LocalTargetBinding valuesTarget, LocalTargetBinding secretsTarget, CancellationToken ct)
    {
        if ((await state.LoadProfileAsync(ct))?.GitIgnoreLocalFiles == true)
        {
            await targets.First(t => t.Kind == valuesTarget.Kind).EnsureGitIgnoredAsync(valuesTarget, ct);
            if (secretsTarget != valuesTarget)
            {
                await targets.First(t => t.Kind == secretsTarget.Kind).EnsureGitIgnoredAsync(secretsTarget, ct);
            }
        }

        var option = await targets.First(t => t.Kind == secretsTarget.Kind).InspectAsync(secretsTarget, ct);
        if (!option.IsSafeForSecrets)
        {
            throw new InvalidOperationException($"{secretsTarget.Kind} target is not safe for secrets.");
        }
    }

    /// <summary>
    /// The binding with the manifest's shared fields (see the class summary); saved only when it changed. Targets that already
    /// match the manifest are kept as they are (no project detection).
    /// </summary>
    private async Task<ProjectBinding> ApplyManifestAsync(string repoPath, ManifestLocation location, ProjectBinding? existing, CancellationToken ct)
    {
        var manifest = location.Manifest;
        var values = await ManifestTargetAsync(location, existing?.ValuesTarget, manifest.Values, "values", ct);
        var secrets = await ManifestTargetAsync(location, existing?.SecretsTarget, manifest.Secrets, "secrets", ct);

        var vaults = new Dictionary<string, Uri>(existing?.VaultByEnvironment ?? new Dictionary<string, Uri>());
        foreach (var (environment, uri) in ManifestBinding.BindingVaults(manifest))
        {
            foreach (var same in vaults.Keys.Where(k => k.Equals(environment, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                vaults.Remove(same);
            }

            vaults[environment] = uri;
        }

        if (existing is null)
        {
            await PrepareTargetsAsync(values, secrets, ct);
            var created = new ProjectBinding(repoPath, manifest.Service, values, secrets, vaults);
            await state.SaveBindingAsync(created, ct);
            return created;
        }

        var sameVaults = vaults.Count == existing.VaultByEnvironment.Count
            && vaults.All(p => existing.VaultByEnvironment.TryGetValue(p.Key, out var old) && old == p.Value);
        var sameTargets = values == existing.ValuesTarget && secrets == existing.SecretsTarget;
        var serviceChanged = !manifest.Service.Equals(existing.Service, StringComparison.Ordinal);
        if (!serviceChanged && sameVaults && sameTargets)
        {
            return existing;
        }

        if (!sameTargets)
        {
            await PrepareTargetsAsync(values, secrets, ct);
        }

        var updated = existing with { ValuesTarget = values, SecretsTarget = secrets, VaultByEnvironment = vaults };
        if (serviceChanged)
        {
            // Snapshots first: if deleting fails the binding stays unchanged and the next command retries.
            await state.DeleteSnapshotsAsync(repoPath, ct);
            updated = updated with { Service = manifest.Service, ActiveEnvironment = null };
        }

        await state.SaveBindingAsync(updated, ct);
        return updated;
    }

    /// <summary><paramref name="current"/> when it already matches <paramref name="shared"/>; otherwise the detected project's target.</summary>
    private async Task<LocalTargetBinding> ManifestTargetAsync(
        ManifestLocation location, LocalTargetBinding? current, ManifestTarget shared, string field, CancellationToken ct)
    {
        if (current is not null && ManifestBinding.Matches(current, shared, location.FilePath))
        {
            return current;
        }

        var folder = ManifestBinding.FolderOf(location.FilePath);
        var kind = ProjectLookup.KindNames.First(k => k.Name == shared.Target).Kind;
        var local = targets.FirstOrDefault(t => t.Kind == kind)
            ?? throw new InvalidOperationException($"'{location.FilePath}': {field}.target '{shared.Target}' is not supported by this azcfg.");
        var project = ProjectLookup.Find(await detector.DetectAsync(folder, ct), folder, shared.Project)
            ?? throw new InvalidOperationException($"'{location.FilePath}': {field}.project '{shared.Project}' does not name a .csproj under the manifest folder.");
        if (!local.CanHandle(project))
        {
            throw new InvalidOperationException($"'{location.FilePath}': {field}.target '{shared.Target}' does not apply to project '{project.Name}'.");
        }

        var format = kind == LocalTargetKind.LocalSettings ? LocalFileFormat.FlatDictionary : LocalFileFormat.NestedJson;
        return new(kind, project.CsprojPath, format);
    }

    public Task<ProjectBinding> AddIgnoreAsync(string repoPath, string pattern, CancellationToken ct) =>
        UpdateIgnoreAsync(repoPath, pattern, (ignore, p) => ignore.Contains(p, StringComparer.OrdinalIgnoreCase) ? ignore : [.. ignore, p], ct);

    public Task<ProjectBinding> RemoveIgnoreAsync(string repoPath, string pattern, CancellationToken ct) =>
        UpdateIgnoreAsync(repoPath, pattern, (ignore, p) => [.. ignore.Where(i => !string.Equals(i, p, StringComparison.OrdinalIgnoreCase))], ct);

    private async Task<ProjectBinding> UpdateIgnoreAsync(
        string repoPath, string pattern, Func<IReadOnlyList<string>, string, IReadOnlyList<string>> update, CancellationToken ct)
    {
        var normalized = IgnoreRules.Normalize(pattern);
        var binding = await state.LoadBindingAsync(repoPath, ct)
            ?? throw new InvalidOperationException("This project is not bound yet. Run 'azcfg pull' first.");
        var updated = binding with { Ignore = update(binding.Ignore ?? [], normalized) };
        await state.SaveBindingAsync(updated, ct);
        return updated;
    }

    private static string Normalize(string name) => name.Replace('-', '.');
}
