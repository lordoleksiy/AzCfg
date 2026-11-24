using AzCfg.Core.Abstractions;
using AzCfg.Core.Manifest;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>
/// Project-scoped settings over the saved <see cref="ProjectBinding"/> (ADR 0014); see <see cref="IProjectSettingsService"/>.
/// Values follow the pull wizard's rules: a service name is valid for <see cref="KeyMapper"/>, a target must be registered, able to handle an
/// existing detected project and (for secrets) safe for secrets; in the <see cref="UserProfile.GitIgnoreLocalFiles"/> mode the target's file
/// is git-ignored first, as <see cref="ProjectService.BindAsync"/> does.
/// When an <c>azcfg.json</c> is found from the project path (ADR 0012 amendment 2) a shared key is written to it as well — the manifest
/// first, so a failed write leaves the binding unchanged — and the result carries <see cref="UserSetting.Source"/> (the manifest path) and
/// <see cref="ProjectSettingKeys.SharedHint"/>. A manifest that cannot be read fails every call with its <see cref="ManifestException"/>.
/// </summary>
public sealed class ProjectSettingsService(
    IUserStateStore state,
    IProjectDetector detector,
    IEnumerable<ILocalTarget> targets,
    IProjectManifestStore manifests) : IProjectSettingsService
{
    public async Task<IReadOnlyList<UserSetting>> ListAsync(string repoPath, CancellationToken ct) =>
        await state.LoadBindingAsync(repoPath, ct) is { } binding
            ? List(binding, await state.LoadProfileAsync(ct), await manifests.FindAsync(repoPath, ct))
            : [];

    public async Task<UserSetting> GetAsync(string repoPath, string key, CancellationToken ct)
    {
        var binding = await LoadAsync(repoPath, ct);
        return Find(List(binding, await state.LoadProfileAsync(ct), await manifests.FindAsync(repoPath, ct)), key);
    }

    public async Task<UserSetting> SetAsync(string repoPath, string key, string? value, CancellationToken ct)
    {
        var binding = await LoadAsync(repoPath, ct);
        var profile = await state.LoadProfileAsync(ct);
        var shared = await manifests.FindAsync(repoPath, ct);

        if (key.Equals(ProjectSettingKeys.Active, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("'project.active' is read-only: use 'azcfg use <ENV>' to change the active environment.");
        }

        if (key.Equals(ProjectSettingKeys.Ignore, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("'project.ignore' is read-only: use 'azcfg ignore add|remove <PATTERN>' to change the ignore patterns.");
        }

        if (key.Equals(ProjectSettingKeys.Shared, StringComparison.OrdinalIgnoreCase))
        {
            return await ShareAsync(repoPath, binding, shared, value, ct);
        }

        if (key.Equals(ProjectSettingKeys.Service, StringComparison.OrdinalIgnoreCase))
        {
            return await SetServiceAsync(binding, shared, value, ct);
        }

        if (key.Equals(ProjectSettingKeys.Values, StringComparison.OrdinalIgnoreCase))
        {
            var values = await ParseTargetAsync(binding, ProjectSettingKeys.Values, value, binding.ValuesTarget, ct);
            if (values != binding.ValuesTarget)
            {
                await PrepareTargetAsync(values, profile, checkSecrets: false, ct);
            }

            await SaveManifestAsync(shared, (m, path) => m with { Values = ManifestBinding.Target(values, path, "values") }, ct);
            if (values != binding.ValuesTarget)
            {
                await state.SaveBindingAsync(binding with { ValuesTarget = values }, ct);
            }

            return WithSource(new(ProjectSettingKeys.Values, Describe(values)), shared);
        }

        if (key.Equals(ProjectSettingKeys.Secrets, StringComparison.OrdinalIgnoreCase))
        {
            var secrets = await ParseTargetAsync(binding, ProjectSettingKeys.Secrets, value, binding.SecretsTarget, ct);
            if (secrets != binding.SecretsTarget)
            {
                await PrepareTargetAsync(secrets, profile, checkSecrets: true, ct);
            }

            await SaveManifestAsync(shared, (m, path) => m with { Secrets = ManifestBinding.Target(secrets, path, "secrets") }, ct);
            if (secrets != binding.SecretsTarget)
            {
                await state.SaveBindingAsync(binding with { SecretsTarget = secrets }, ct);
            }

            return WithSource(new(ProjectSettingKeys.Secrets, Describe(secrets)), shared);
        }

        if (key.StartsWith(ProjectSettingKeys.VaultPrefix, StringComparison.OrdinalIgnoreCase) && key.Length > ProjectSettingKeys.VaultPrefix.Length)
        {
            return await SetVaultAsync(binding, profile, shared, key, value, ct);
        }

        throw UnknownKey(key, List(binding, profile, shared));
    }

    /// <summary>
    /// <c>project.shared true</c>: writes the binding's shared fields to the manifest in effect, else to <c>azcfg.json</c> in
    /// <paramref name="repoPath"/> (the project path); an unchanged manifest is not rewritten. Unsharing = deleting the file.
    /// </summary>
    private async Task<UserSetting> ShareAsync(string repoPath, ProjectBinding binding, ManifestLocation? shared, string? value, CancellationToken ct)
    {
        if (!string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            var file = shared?.FilePath ?? IProjectManifestStore.FileName;
            throw new ArgumentException(
                $"Invalid value '{value}' for '{ProjectSettingKeys.Shared}': it can only be set to 'true'. To stop sharing, delete '{file}' and commit.");
        }

        var path = shared?.FilePath ?? Path.Combine(repoPath, IProjectManifestStore.FileName);
        var manifest = ManifestBinding.FromBinding(binding, path);
        if (shared?.Manifest != manifest)
        {
            await manifests.SaveAsync(path, manifest, ct);
        }

        return new(ProjectSettingKeys.Shared, "true") { Source = path, Hint = ProjectSettingKeys.SharedHint };
    }

    /// <summary>Writes <paramref name="update"/>(manifest, path) when a manifest is in effect and the result differs from it.</summary>
    private async Task SaveManifestAsync(ManifestLocation? shared, Func<ProjectManifest, string, ProjectManifest> update, CancellationToken ct)
    {
        if (shared is null)
        {
            return;
        }

        var updated = update(shared.Manifest, shared.FilePath);
        if (updated != shared.Manifest)
        {
            await manifests.SaveAsync(shared.FilePath, updated, ct);
        }
    }

    private static UserSetting WithSource(UserSetting setting, ManifestLocation? shared) =>
        shared is null ? setting : setting with { Source = shared.FilePath, Hint = ProjectSettingKeys.SharedHint };

    private async Task<UserSetting> SetServiceAsync(ProjectBinding binding, ManifestLocation? shared, string? value, CancellationToken ct)
    {
        var service = value?.Trim() ?? "";
        try
        {
            _ = KeyMapper.ToStoreKey(ConfigScope.Service, service, "x");
        }
        catch (ArgumentException e)
        {
            throw Invalid(ProjectSettingKeys.Service, value, $"a valid service name ({e.Message.Split(" (Parameter", 2)[0]})");
        }

        // Manifest first: if the binding is not saved afterwards, the next resolution applies the same rule from the manifest.
        await SaveManifestAsync(shared, (m, _) => m with { Service = service }, ct);
        if (service != binding.Service)
        {
            // Snapshots first: if deleting fails the binding stays unchanged and the change can be retried.
            await state.DeleteSnapshotsAsync(binding.RepoPath, ct);
            await state.SaveBindingAsync(binding with { Service = service, ActiveEnvironment = null }, ct);
        }

        return WithSource(new(ProjectSettingKeys.Service, service), shared);
    }

    private async Task<UserSetting> SetVaultAsync(
        ProjectBinding binding, UserProfile? profile, ManifestLocation? shared, string key, string? value, CancellationToken ct)
    {
        var requested = key[ProjectSettingKeys.VaultPrefix.Length..];
        var name = (profile?.Environments.Select(e => e.Name) ?? [])
            .Concat(binding.VaultByEnvironment.Keys)
            .FirstOrDefault(n => n.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? throw UnknownKey(key, List(binding, profile, shared));
        var canonical = ProjectSettingKeys.VaultPrefix + name;

        var vaults = new Dictionary<string, Uri>(binding.VaultByEnvironment);
        Uri? vault = string.IsNullOrWhiteSpace(value) ? null : NormalizeVault(canonical, value);
        if (vault is null)
        {
            vaults.Remove(name);
        }
        else
        {
            vaults[name] = vault;
        }

        await SaveManifestAsync(shared, (m, _) => m with { Vaults = WithVault(m.Vaults, name, vault) }, ct);
        var before = binding.VaultByEnvironment.TryGetValue(name, out var old) ? old : null;
        if (before != vault)
        {
            await state.SaveBindingAsync(binding with { VaultByEnvironment = vaults }, ct);
        }

        return WithSource(new(canonical, vault?.OriginalString), shared);
    }

    /// <summary>The manifest's vaults with <paramref name="environment"/> (any case) set to <paramref name="vault"/> or removed; <c>null</c> when empty.</summary>
    private static IReadOnlyDictionary<string, Uri>? WithVault(IReadOnlyDictionary<string, Uri>? vaults, string environment, Uri? vault)
    {
        var updated = (vaults ?? new Dictionary<string, Uri>())
            .Where(p => !p.Key.Equals(environment, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.Key, p => p.Value);
        if (vault is not null)
        {
            updated[environment] = vault;
        }

        return ManifestBinding.Vaults(updated);
    }

    /// <summary>A vault is its root URI: https, absolute, no path (or "/"), query, fragment or user info; stored without the trailing slash.</summary>
    private static Uri NormalizeVault(string key, string value)
    {
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.AbsolutePath == "/"
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0
            && uri.UserInfo.Length == 0)
        {
            return new(uri.GetLeftPart(UriPartial.Authority));
        }

        throw Invalid(key, value, "an absolute https Key Vault root URI without path, query or fragment, e.g. https://<name>.vault.azure.net");
    }

    /// <summary>Git-ignores the target's file in the gitignore mode and, for secrets, rejects a target that is not safe for secrets.</summary>
    private async Task PrepareTargetAsync(LocalTargetBinding target, UserProfile? profile, bool checkSecrets, CancellationToken ct)
    {
        var local = targets.First(t => t.Kind == target.Kind);
        if (profile?.GitIgnoreLocalFiles == true)
        {
            await local.EnsureGitIgnoredAsync(target, ct);
        }

        if (checkSecrets && !(await local.InspectAsync(target, ct)).IsSafeForSecrets)
        {
            throw new InvalidOperationException($"{target.Kind} target is not safe for secrets.");
        }
    }

    /// <summary>Parses <c>&lt;target&gt;[:&lt;project path&gt;]</c> into a binding; the path defaults to <paramref name="current"/>'s.</summary>
    private async Task<LocalTargetBinding> ParseTargetAsync(ProjectBinding binding, string key, string? value, LocalTargetBinding current, CancellationToken ct)
    {
        var text = value?.Trim() ?? "";
        var colon = text.IndexOf(':');
        var kindName = colon < 0 ? text : text[..colon];
        var path = colon < 0 ? "" : text[(colon + 1)..].Trim();

        var kind = ProjectLookup.KindNames.FirstOrDefault(k => k.Name.Equals(kindName, StringComparison.OrdinalIgnoreCase));
        if (kind.Name is null)
        {
            throw Invalid(key, value, "<target>[:<project path>] with target user-secrets, appsettings-local or local-settings");
        }

        var local = targets.FirstOrDefault(t => t.Kind == kind.Kind)
            ?? throw new ArgumentException($"Invalid value '{value}' for '{key}': target '{kind.Name}' is not supported yet.");

        var shown = path.Length > 0 ? path : current.ProjectPath;
        var projects = await detector.DetectAsync(binding.RepoPath, ct);
        var project = ProjectLookup.Find(projects, binding.RepoPath, shown)
            ?? throw new ArgumentException($"Invalid value '{value}' for '{key}': no .csproj found at '{shown}' under the repository.");

        if (!local.CanHandle(project))
        {
            throw new ArgumentException($"Invalid value '{value}' for '{key}': target '{kind.Name}' does not apply to project '{project.Name}'.");
        }

        var format = kind.Kind == LocalTargetKind.LocalSettings ? LocalFileFormat.FlatDictionary : LocalFileFormat.NestedJson;
        return new(kind.Kind, project.CsprojPath, format);
    }

    private async Task<ProjectBinding> LoadAsync(string repoPath, CancellationToken ct) =>
        await state.LoadBindingAsync(repoPath, ct) ?? throw new InvalidOperationException("This project is not bound yet. Run 'azcfg pull' first.");

    private static List<UserSetting> List(ProjectBinding binding, UserProfile? profile, ManifestLocation? shared)
    {
        var vaults = (profile?.Environments.Select(e => e.Name) ?? [])
            .Concat(binding.VaultByEnvironment.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new UserSetting(
                ProjectSettingKeys.VaultPrefix + name,
                binding.VaultByEnvironment.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value?.OriginalString));
        return
        [
            new(ProjectSettingKeys.Service, binding.Service),
            new(ProjectSettingKeys.Values, Describe(binding.ValuesTarget)),
            new(ProjectSettingKeys.Secrets, Describe(binding.SecretsTarget)),
            .. vaults,
            shared is null ? new(ProjectSettingKeys.Shared, "false") : new(ProjectSettingKeys.Shared, "true") { Source = shared.FilePath },
            new(ProjectSettingKeys.Active, binding.ActiveEnvironment),
            new(ProjectSettingKeys.Ignore, binding.Ignore is { Count: > 0 } ignore ? string.Join(",", ignore) : null),
        ];
    }

    private static string Describe(LocalTargetBinding target) => ProjectSettingKeys.TargetValue(target);

    private static UserSetting Find(List<UserSetting> settings, string key) =>
        settings.Find(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? throw UnknownKey(key, settings);

    private static ArgumentException Invalid(string key, string? value, string expected) =>
        new($"Invalid value '{value}' for '{key}': expected {expected}.");

    private static ArgumentException UnknownKey(string key, IEnumerable<UserSetting> settings) =>
        new($"Unknown setting '{key}'. Known project settings: {string.Join(", ", settings.Select(s => s.Key))}.");
}
