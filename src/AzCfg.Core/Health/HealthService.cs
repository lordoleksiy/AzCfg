using AzCfg.Core.Abstractions;
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
    ILogger<HealthService>? logger = null) : IHealthService
{
    private const string GitIgnoreFix = "azcfg config set gitignoreLocalFiles true";
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
            return new(who, [.. environments.Select(e => new EnvironmentCheck(e.Name, false, "Not signed in to Azure."))], findings);
        }

        List<EnvironmentCheck> checks = [];
        foreach (var env in environments)
        {
            var check = await CheckEnvironmentAsync(env, binding, ct);
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
        }

        return new(who, checks, findings);
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

    private async Task<EnvironmentCheck> CheckEnvironmentAsync(ConfigEnvironment env, ProjectBinding? binding, CancellationToken ct)
    {
        try
        {
            if (binding is null)
            {
                await foreach (var _ in store.ListServicePrefixesAsync(env, ct).WithCancellation(ct))
                {
                    break;
                }
            }
            else
            {
                await foreach (var _ in store.ReadAsync(env, binding.Service, ct).WithCancellation(ct))
                {
                    break;
                }
            }

            return new(env.Name, true, null);
        }
        catch (Exception ex) when (Handled(ex, ct, "environment access"))
        {
            return new(env.Name, false, ex.GetType().Name);
        }
    }

    private async Task CheckTargetsAsync(ProjectBinding binding, List<HealthFinding> findings, CancellationToken ct)
    {
        var secretsSafe = await IsSafeAsync(binding.SecretsTarget, ct);
        if (secretsSafe != true)
        {
            findings.Add(secretsSafe is false
                ? new(HealthCheckKind.SecretsTargetNotIgnored, HealthSeverity.Error, $"The secrets target ({binding.SecretsTarget.Kind}) is not safe for secrets: it is tracked or not git-ignored.", Fix: GitIgnoreFix)
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
                    $"azcfg promote {missing.PresentIn[0]} {missing.MissingIn[0]}"));
            }
        }
    }

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
