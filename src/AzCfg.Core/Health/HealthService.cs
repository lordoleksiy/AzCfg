using AzCfg.Core.Abstractions;
using AzCfg.Core.Manifest;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzCfg.Core.Health;

/// <summary>
/// Runs the configuration health checks of ADR 0013 (amendment, CLI 0.5.0). Each check is isolated: a failure becomes a finding
/// (cancellation excepted). Only exception type names are logged and reported, never messages, so no value can leak.
/// </summary>
public sealed class HealthService(
    IUserStateStore state,
    IEnvironmentResolver resolver,
    IConfigStore store,
    ISyncService sync,
    ISecretStore secrets,
    IAzureAccountProbe account,
    IEnumerable<ILocalTarget> targets,
    IProjectDetector detector,
    IAccessErrorClassifier classifier,
    ITrackedSettingsReader trackedSettings,
    IProjectManifestStore manifests,
    ILogger<HealthService>? logger = null) : IHealthService
{
    private const string GitIgnoreFix = "azcfg config set gitignoreLocalFiles true";
    private const string UpdateFix = "dotnet tool update --global AzCfg";
    private static readonly string[] SecretLikeMarkers = ["password", "secret", "token", "apikey", "connectionstring"];
    private static readonly string[] SecretSections = ["ApiKeys", "Secrets", "Tokens", "Passwords", "ConnectionStrings"];
    private readonly ILogger _log = logger ?? NullLogger<HealthService>.Instance;

    public async Task<HealthReport> CheckAsync(string repoPath, CancellationToken ct)
    {
        var profile = await state.LoadProfileAsync(ct) ?? throw new InvalidOperationException("No profile found. Run 'azcfg init' first.");
        var environments = resolver.Resolve(profile);
        var binding = await state.LoadBindingAsync(repoPath, ct);
        List<HealthFinding> findings = [];

        var who = await ProbeAccountAsync(ct);
        if (!who.SignedIn)
        {
            findings.Add(new(HealthCheckKind.AzureSignIn, HealthSeverity.Error, $"Not signed in to Azure{Suffix(who.Error)}.", Fix: "az login"));
            if (binding is not null)
            {
                // Local checks need no Azure access.
                await CheckTargetsAsync(binding, findings, ct);
                await CheckTargetProjectsAsync(binding, findings, ct);
            }

            await CheckManifestAsync(repoPath, binding, findings, ct);
            await CheckTrackedSettingsAsync(repoPath, findings, ct);
            return new(who, [.. environments.Select(e => new EnvironmentCheck(e.Name, EnvironmentAccess.AuthRequired, "Not signed in to Azure."))], findings);
        }

        List<EnvironmentCheck> checks = [];
        foreach (var env in environments)
        {
            var check = await CheckEnvironmentAsync(env, binding?.Service, ct);
            checks.Add(check);
            if (!check.Reachable)
            {
                findings.Add(new(HealthCheckKind.EnvironmentAccess, HealthSeverity.Error, $"Environment '{env.Name}' is not reachable{Suffix(check.Error)}.", env.Name));
            }
        }

        if (binding is not null)
        {
            var reachable = environments.Where(e => checks.First(c => c.Name == e.Name).Reachable).ToList();
            await CheckTargetsAsync(binding, findings, ct);
            await CheckRemoteAsync(binding, reachable, findings, ct);
            await CheckBindingAsync(binding, environments, reachable, findings, ct);
        }

        await CheckManifestAsync(repoPath, binding, findings, ct);
        await CheckTrackedSettingsAsync(repoPath, findings, ct);
        return new(who, checks, findings);
    }

    /// <summary>
    /// A project manifest <c>azcfg.json</c> is unreadable, fails the schema or needs a newer azcfg (ADR 0012, health). The nearest
    /// manifest is looked up from <paramref name="repoPath"/>, from each bound target project and from each detected project, so a
    /// per-service manifest is found from the repository root; one finding per distinct file. The finding carries the file path
    /// and the manifest error messages, which never contain field values; absent or valid → no finding. Any other failure of the
    /// store → one warning that the check was skipped (exception type only).
    /// </summary>
    private async Task CheckManifestAsync(string repoPath, ProjectBinding? binding, List<HealthFinding> findings, CancellationToken ct)
    {
        HashSet<string> reported = new(StringComparer.Ordinal);
        var skipped = false;
        foreach (var path in await ManifestSearchPathsAsync(repoPath, binding, ct))
        {
            try
            {
                await manifests.FindAsync(path, ct);
            }
            catch (ManifestException ex)
            {
                var file = ex.FilePath ?? IProjectManifestStore.FileName;
                if (reported.Add(file))
                {
                    findings.Add(ManifestFinding(ex, file));
                }
            }
            catch (Exception ex) when (Handled(ex, ct, "manifest"))
            {
                if (!skipped)
                {
                    skipped = true;
                    findings.Add(new(HealthCheckKind.ManifestInvalid, HealthSeverity.Warning, $"The project manifest check was skipped ({ex.GetType().Name})."));
                }
            }
        }
    }

    private static HealthFinding ManifestFinding(ManifestException ex, string file)
    {
        var details = string.Join("; ", ex.Errors.Select(e => e.Message));
        return ex.Kind switch
        {
            ManifestErrorKind.Unreadable => new(HealthCheckKind.ManifestInvalid, HealthSeverity.Error, $"The project manifest '{file}' is unreadable: {details} Fix the JSON or delete the file."),
            ManifestErrorKind.UnsupportedVersion => new(HealthCheckKind.ManifestInvalid, HealthSeverity.Error, $"The project manifest '{file}' needs a newer azcfg: {details}", Fix: UpdateFix),
            _ => new(HealthCheckKind.ManifestInvalid, HealthSeverity.Error, $"The project manifest '{file}' does not match the schema: {details}"),
        };
    }

    /// <summary>The repository path, the bound target projects and the detected projects, without duplicates.</summary>
    private async Task<IReadOnlyList<string>> ManifestSearchPathsAsync(string repoPath, ProjectBinding? binding, CancellationToken ct)
    {
        List<string> paths = [repoPath];
        if (binding is not null)
        {
            paths.Add(Resolve(binding.RepoPath, binding.ValuesTarget.ProjectPath));
            paths.Add(Resolve(binding.RepoPath, binding.SecretsTarget.ProjectPath));
        }

        try
        {
            paths.AddRange((await detector.DetectAsync(repoPath, ct)).Select(p => Resolve(repoPath, p.CsprojPath)));
        }
        catch (Exception ex) when (Handled(ex, ct, "manifest project lookup"))
        {
        }

        return [.. paths.Distinct(StringComparer.Ordinal)];
    }

    private static string Resolve(string root, string path) => Path.IsPathRooted(path) ? path : Path.Combine(root, path);

    /// <summary>
    /// A git-tracked <c>appsettings*.json</c> with a value under a secret-like key (see <see cref="IsSecretLike"/>; ADR 0012, health).
    /// The reader never returns values; the finding names the file and the key only.
    /// </summary>
    private async Task CheckTrackedSettingsAsync(string repoPath, List<HealthFinding> findings, CancellationToken ct)
    {
        try
        {
            var keys = await trackedSettings.ReadKeysAsync(repoPath, ct);
            foreach (var key in keys.Where(k => k.HasValue && IsSecretLike(k.Key)))
            {
                findings.Add(new(
                    HealthCheckKind.SecretLikeKeyInTrackedFile,
                    HealthSeverity.Warning,
                    $"'{key.File}' is tracked by git and has a value under secret-like key '{key.Key}'. Move it to user-secrets or Key Vault."));
            }
        }
        catch (Exception ex) when (Handled(ex, ct, "tracked settings"))
        {
        }
    }

    /// <summary>
    /// The last non-numeric key segment names a secret (<c>Db:Password</c>, <c>ApiKeys:0</c>), or a parent section has the plural
    /// section form (<c>Secrets:Github</c>, <c>Tokens:Admin</c>, <c>ConnectionStrings:Default</c>). A singular parent alone does
    /// not count (<c>Auth:Token:Issuer</c>).
    /// </summary>
    private static bool IsSecretLike(string key)
    {
        var segments = key.Split(':').Where(s => !s.All(char.IsAsciiDigit)).ToArray();
        return segments.Length > 0
            && (SecretLikeMarkers.Any(m => segments[^1].Contains(m, StringComparison.OrdinalIgnoreCase))
                || segments[..^1].Any(s => SecretSections.Contains(s, StringComparer.OrdinalIgnoreCase)));
    }

    private async Task<AzureAccountCheck> ProbeAccountAsync(CancellationToken ct)
    {
        try
        {
            return await account.CheckAsync(ct);
        }
        catch (Exception ex) when (Handled(ex, ct, "account probe"))
        {
            return new(false, null, ex.GetType().Name);
        }
    }

    public async Task<EnvironmentCheck> CheckEnvironmentAsync(ConfigEnvironment env, string? service, CancellationToken ct)
    {
        try
        {
            if (service is null)
            {
                await foreach (var _ in store.ListServicePrefixesAsync(env, ct).WithCancellation(ct))
                {
                    break;
                }
            }
            else
            {
                await foreach (var _ in store.ReadAsync(env, service, ct).WithCancellation(ct))
                {
                    break;
                }
            }

            return new(env.Name, EnvironmentAccess.Ok, null);
        }
        catch (Exception ex) when (Handled(ex, ct, "environment access"))
        {
            var failure = classifier.Classify(ex);
            return new(env.Name, failure.Access, failure.Reason);
        }
    }

    private async Task CheckTargetsAsync(ProjectBinding binding, List<HealthFinding> findings, CancellationToken ct)
    {
        var secretsSafe = await IsSafeAsync(binding.SecretsTarget, ct);
        if (secretsSafe != true)
        {
            findings.Add(secretsSafe is false
                ? new(HealthCheckKind.SecretsTargetNotIgnored, HealthSeverity.Error, $"The secrets target ({binding.SecretsTarget.Kind}) is not safe for secrets: it is tracked or not git-ignored. If the file is tracked, untrack it (git rm --cached <file>) and run 'azcfg pull'.", Fix: GitIgnoreFix)
                : new(HealthCheckKind.SecretsTargetNotIgnored, HealthSeverity.Warning, $"The secrets target ({binding.SecretsTarget.Kind}) could not be inspected.", Fix: GitIgnoreFix));
        }

        if (binding.ValuesTarget != binding.SecretsTarget && await IsSafeAsync(binding.ValuesTarget, ct) == false)
        {
            findings.Add(new(HealthCheckKind.ValuesTargetNotIgnored, HealthSeverity.Warning, $"The values target ({binding.ValuesTarget.Kind}) is not git-ignored.", Fix: GitIgnoreFix));
        }
    }

    /// <summary><c>null</c> when the target could not be inspected.</summary>
    private async Task<bool?> IsSafeAsync(LocalTargetBinding target, CancellationToken ct)
    {
        try
        {
            var local = targets.FirstOrDefault(t => t.Kind == target.Kind) ?? throw new InvalidOperationException("No local target of this kind.");
            return (await local.InspectAsync(target, ct)).IsSafeForSecrets;
        }
        catch (Exception ex) when (Handled(ex, ct, "target inspection"))
        {
            return null;
        }
    }

    private async Task CheckRemoteAsync(ProjectBinding binding, IReadOnlyList<ConfigEnvironment> reachable, List<HealthFinding> findings, CancellationToken ct)
    {
        List<RemoteConfig> configs = [];
        foreach (var env in reachable)
        {
            try
            {
                configs.Add(await sync.ReadRemoteConfigAsync(binding, env, ct));
            }
            catch (Exception ex) when (Handled(ex, ct, "remote read"))
            {
                findings.Add(new(HealthCheckKind.EnvironmentAccess, HealthSeverity.Error, $"Configuration of '{env.Name}' could not be read ({ex.GetType().Name}).", env.Name));
            }
        }

        var exists = new Dictionary<Uri, bool?>();
        foreach (var config in configs)
        {
            foreach (var entry in config.Entries.Where(e => e.Value is ConfigValue.KeyVaultRef && !IgnoreRules.IsIgnored(binding.Ignore, e.Key, e.Scope)))
            {
                var uri = ((ConfigValue.KeyVaultRef)entry.Value).SecretUri;
                if (!exists.TryGetValue(uri, out var found))
                {
                    exists[uri] = found = await ExistsAsync(uri, ct);
                }

                var envName = config.Environment.Name;
                if (found is false)
                {
                    findings.Add(new(HealthCheckKind.DeadSecretReference, HealthSeverity.Error, $"Key Vault secret of '{entry.Key}' in '{envName}' does not exist.", envName, entry.Key, $"azcfg edit --env {envName}"));
                }
                else if (found is null)
                {
                    findings.Add(new(HealthCheckKind.SecretNotVerified, HealthSeverity.Warning, $"Key Vault secret of '{entry.Key}' in '{envName}' could not be verified (no access?).", envName, entry.Key));
                }
            }
        }

        if (configs.Count >= 2)
        {
            var filtered = configs.Select(c => c with { Entries = [.. c.Entries.Where(e => !IgnoreRules.IsIgnored(binding.Ignore, e.Key, e.Scope))] }).ToList();
            foreach (var missing in MissingKeysReport.Compute(filtered))
            {
                findings.Add(new(
                    HealthCheckKind.KeyMissingInEnvironments,
                    HealthSeverity.Warning,
                    $"Key '{missing.Key}' is missing in {string.Join(", ", missing.MissingIn)}.",
                    string.Join(", ", missing.MissingIn),
                    missing.Key,
                    FixMissing(missing, filtered)));
            }
        }
    }

    /// <summary>Promote copies only service-scope keys; a global key is added with an edit of the global scope.</summary>
    private static string? FixMissing(MissingKey missing, IReadOnlyList<RemoteConfig> configs)
    {
        var isGlobal = configs.SelectMany(c => c.Entries).Any(e => e.Key == missing.Key && e.Scope == ConfigScope.Global);
        return isGlobal ? $"azcfg edit --env {missing.MissingIn[0]} --global --set {missing.Key}=<VALUE>" : $"azcfg promote {missing.PresentIn[0]} {missing.MissingIn[0]}";
    }

    /// <summary>The binding no longer matches reality: the service has no keys, a target's project is gone, a vault names an unknown environment.</summary>
    private async Task CheckBindingAsync(
        ProjectBinding binding,
        IReadOnlyList<ConfigEnvironment> environments,
        IReadOnlyList<ConfigEnvironment> reachable,
        List<HealthFinding> findings,
        CancellationToken ct)
    {
        await CheckServiceAsync(binding, reachable, findings, ct);
        await CheckTargetProjectsAsync(binding, findings, ct);

        foreach (var name in binding.VaultByEnvironment.Keys.Where(n => !environments.Any(e => e.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
        {
            findings.Add(new(HealthCheckKind.BindingMismatch, HealthSeverity.Warning, $"A Key Vault is bound to environment '{name}', which is not configured.", name, Fix: $"azcfg config set project.vault.{name} \"\""));
        }
    }

    private async Task CheckServiceAsync(ProjectBinding binding, IReadOnlyList<ConfigEnvironment> reachable, List<HealthFinding> findings, CancellationToken ct)
    {
        try
        {
            if (reachable.Count == 0)
            {
                return;
            }

            HashSet<string> others = [];
            foreach (var env in reachable)
            {
                var prefixes = await store.ListServicePrefixesAsync(env, ct).ToListAsync(ct);
                if (prefixes.Contains(binding.Service, StringComparer.Ordinal))
                {
                    return;
                }

                others.UnionWith(prefixes);
            }

            var suggestion = others.Count == 1 ? others.Single() : "<SERVICE>";
            findings.Add(new(HealthCheckKind.BindingMismatch, HealthSeverity.Warning, $"Service '{binding.Service}' has no keys in any reachable environment.", Fix: $"azcfg config set project.service {suggestion}"));
        }
        catch (Exception ex) when (Handled(ex, ct, "service check"))
        {
        }
    }

    private async Task CheckTargetProjectsAsync(ProjectBinding binding, List<HealthFinding> findings, CancellationToken ct)
    {
        try
        {
            var projects = await detector.DetectAsync(binding.RepoPath, ct);
            var checkValues = binding.ValuesTarget != binding.SecretsTarget;
            if (ProjectLookup.Find(projects, binding.RepoPath, binding.SecretsTarget.ProjectPath) is null)
            {
                findings.Add(MissingProject(binding.SecretsTarget, "project.secrets"));
            }

            if (checkValues && ProjectLookup.Find(projects, binding.RepoPath, binding.ValuesTarget.ProjectPath) is null)
            {
                findings.Add(MissingProject(binding.ValuesTarget, "project.values"));
            }
        }
        catch (Exception ex) when (Handled(ex, ct, "project check"))
        {
        }
    }

    private static HealthFinding MissingProject(LocalTargetBinding target, string setting) =>
        new(
            HealthCheckKind.BindingMismatch,
            HealthSeverity.Error,
            $"The project of the {setting[8..]} target ({target.Kind}) no longer exists: {target.ProjectPath}.",
            Fix: $"azcfg config set {setting} {ProjectLookup.KindNames.First(k => k.Kind == target.Kind).Name}:<PATH>");

    /// <summary><c>null</c> when existence could not be verified.</summary>
    private async Task<bool?> ExistsAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            return await secrets.ExistsAsync(uri, ct);
        }
        catch (Exception ex) when (Handled(ex, ct, "secret check"))
        {
            return null;
        }
    }

    /// <summary>Logs the type name only and tells a <c>catch</c> filter to handle the exception, unless it is a cancellation of <paramref name="ct"/>.</summary>
    private bool Handled(Exception ex, CancellationToken ct, string check)
    {
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
        {
            return false;
        }

        _log.LogWarning("Health check '{Check}' failed: {ExceptionType}", check, ex.GetType().Name);
        return true;
    }

    private static string Suffix(string? error) => string.IsNullOrWhiteSpace(error) ? "" : $" ({error})";
}
