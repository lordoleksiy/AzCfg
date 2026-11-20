using AzCfg.Core.Abstractions;
using AzCfg.Core.Mapping;
using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>
/// Project-scoped settings over the saved <see cref="ProjectBinding"/> (ADR 0014); see <see cref="IProjectSettingsService"/>.
/// Values follow the pull wizard's rules: a service name is valid for <see cref="KeyMapper"/>, a target must be registered, able to handle an
/// existing detected project and (for secrets) safe for secrets; in the <see cref="UserProfile.GitIgnoreLocalFiles"/> mode the target's file
/// is git-ignored first, as <see cref="ProjectService.BindAsync"/> does.
/// </summary>
public sealed class ProjectSettingsService(IUserStateStore state, IProjectDetector detector, IEnumerable<ILocalTarget> targets) : IProjectSettingsService
{
    private static readonly (string Name, LocalTargetKind Kind)[] KindNames =
    [
        ("user-secrets", LocalTargetKind.UserSecrets),
        ("appsettings-local", LocalTargetKind.AppSettingsLocal),
        ("local-settings", LocalTargetKind.LocalSettings),
    ];

    public async Task<IReadOnlyList<UserSetting>> ListAsync(string repoPath, CancellationToken ct) =>
        await state.LoadBindingAsync(repoPath, ct) is { } binding ? List(binding, await state.LoadProfileAsync(ct)) : [];

    public async Task<UserSetting> GetAsync(string repoPath, string key, CancellationToken ct)
    {
        var binding = await LoadAsync(repoPath, ct);
        return Find(List(binding, await state.LoadProfileAsync(ct)), key);
    }

    public async Task<UserSetting> SetAsync(string repoPath, string key, string? value, CancellationToken ct)
    {
        var binding = await LoadAsync(repoPath, ct);
        var profile = await state.LoadProfileAsync(ct);

        if (key.Equals(ProjectSettingKeys.Active, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("'project.active' is read-only: use 'azcfg use <ENV>' to change the active environment.");
        }

        if (key.Equals(ProjectSettingKeys.Ignore, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("'project.ignore' is read-only: use 'azcfg ignore add|remove <PATTERN>' to change the ignore patterns.");
        }

        if (key.Equals(ProjectSettingKeys.Service, StringComparison.OrdinalIgnoreCase))
        {
            return await SetServiceAsync(binding, value, ct);
        }

        if (key.Equals(ProjectSettingKeys.Values, StringComparison.OrdinalIgnoreCase))
        {
            var values = await ParseTargetAsync(binding, ProjectSettingKeys.Values, value, binding.ValuesTarget, ct);
            if (values != binding.ValuesTarget)
            {
                await PrepareTargetAsync(values, profile, checkSecrets: false, ct);
                await state.SaveBindingAsync(binding with { ValuesTarget = values }, ct);
            }

            return new(ProjectSettingKeys.Values, Describe(values));
        }

        if (key.Equals(ProjectSettingKeys.Secrets, StringComparison.OrdinalIgnoreCase))
        {
            var secrets = await ParseTargetAsync(binding, ProjectSettingKeys.Secrets, value, binding.SecretsTarget, ct);
            if (secrets != binding.SecretsTarget)
            {
                await PrepareTargetAsync(secrets, profile, checkSecrets: true, ct);
                await state.SaveBindingAsync(binding with { SecretsTarget = secrets }, ct);
            }

            return new(ProjectSettingKeys.Secrets, Describe(secrets));
        }

        if (key.StartsWith(ProjectSettingKeys.VaultPrefix, StringComparison.OrdinalIgnoreCase) && key.Length > ProjectSettingKeys.VaultPrefix.Length)
        {
            return await SetVaultAsync(binding, profile, key, value, ct);
        }

        throw UnknownKey(key, List(binding, profile));
    }

    private async Task<UserSetting> SetServiceAsync(ProjectBinding binding, string? value, CancellationToken ct)
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

        if (service != binding.Service)
        {
            await state.SaveBindingAsync(binding with { Service = service, ActiveEnvironment = null }, ct);
            await state.DeleteSnapshotsAsync(binding.RepoPath, ct);
        }

        return new(ProjectSettingKeys.Service, service);
    }

    private async Task<UserSetting> SetVaultAsync(ProjectBinding binding, UserProfile? profile, string key, string? value, CancellationToken ct)
    {
        var requested = key[ProjectSettingKeys.VaultPrefix.Length..];
        var name = (profile?.Environments.Select(e => e.Name) ?? [])
            .Concat(binding.VaultByEnvironment.Keys)
            .FirstOrDefault(n => n.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? throw UnknownKey(key, List(binding, profile));
        var canonical = ProjectSettingKeys.VaultPrefix + name;

        var vaults = new Dictionary<string, Uri>(binding.VaultByEnvironment);
        if (string.IsNullOrWhiteSpace(value))
        {
            vaults.Remove(name);
        }
        else
        {
            vaults[name] = Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                ? uri
                : throw Invalid(canonical, value, "an absolute https Key Vault URI, e.g. https://<name>.vault.azure.net");
        }

        var before = binding.VaultByEnvironment.TryGetValue(name, out var old) ? old : null;
        if (before != vaults.GetValueOrDefault(name))
        {
            await state.SaveBindingAsync(binding with { VaultByEnvironment = vaults }, ct);
        }

        return new(canonical, vaults.TryGetValue(name, out var saved) ? saved.OriginalString : null);
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

        var kind = KindNames.FirstOrDefault(k => k.Name.Equals(kindName, StringComparison.OrdinalIgnoreCase));
        if (kind.Name is null)
        {
            throw Invalid(key, value, "<target>[:<project path>] with target user-secrets, appsettings-local or local-settings");
        }

        var local = targets.FirstOrDefault(t => t.Kind == kind.Kind)
            ?? throw new ArgumentException($"Invalid value '{value}' for '{key}': target '{kind.Name}' is not supported yet.");

        var shown = path.Length > 0 ? path : current.ProjectPath;
        var wanted = Full(binding.RepoPath, shown);
        var projects = await detector.DetectAsync(binding.RepoPath, ct);
        var project = projects.FirstOrDefault(p => SamePath(Full(binding.RepoPath, p.CsprojPath), wanted))
            ?? OnlyIn(projects, binding.RepoPath, wanted)
            ?? throw new ArgumentException($"Invalid value '{value}' for '{key}': no .csproj found at '{shown}' under the repository.");

        if (!local.CanHandle(project))
        {
            throw new ArgumentException($"Invalid value '{value}' for '{key}': target '{kind.Name}' does not apply to project '{project.Name}'.");
        }

        var format = kind.Kind == LocalTargetKind.LocalSettings ? LocalFileFormat.FlatDictionary : LocalFileFormat.NestedJson;
        return new(kind.Kind, project.CsprojPath, format);
    }

    /// <summary>The single detected project in directory <paramref name="directory"/>, if there is exactly one.</summary>
    private static ProjectInfo? OnlyIn(IReadOnlyList<ProjectInfo> projects, string repoPath, string directory) =>
        projects.Where(p => SamePath(Full(repoPath, Path.GetDirectoryName(p.CsprojPath) ?? ""), directory)).ToList() is [var only] ? only : null;

    private static string Full(string repoPath, string path) => Path.GetFullPath(path, Path.GetFullPath(repoPath));

    private static bool SamePath(string a, string b) =>
        string.Equals(
            a.TrimEnd(Path.DirectorySeparatorChar),
            b.TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private async Task<ProjectBinding> LoadAsync(string repoPath, CancellationToken ct) =>
        await state.LoadBindingAsync(repoPath, ct) ?? throw new InvalidOperationException("This project is not bound yet. Run 'azcfg pull' first.");

    private static List<UserSetting> List(ProjectBinding binding, UserProfile? profile)
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
            new(ProjectSettingKeys.Active, binding.ActiveEnvironment),
            new(ProjectSettingKeys.Ignore, binding.Ignore is { Count: > 0 } ignore ? string.Join(",", ignore) : null),
        ];
    }

    private static string Describe(LocalTargetBinding target) =>
        $"{KindNames.First(k => k.Kind == target.Kind).Name}:{target.ProjectPath}";

    private static UserSetting Find(List<UserSetting> settings, string key) =>
        settings.Find(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? throw UnknownKey(key, settings);

    private static ArgumentException Invalid(string key, string? value, string expected) =>
        new($"Invalid value '{value}' for '{key}': expected {expected}.");

    private static ArgumentException UnknownKey(string key, IEnumerable<UserSetting> settings) =>
        new($"Unknown setting '{key}'. Known project settings: {string.Join(", ", settings.Select(s => s.Key))}.");
}
