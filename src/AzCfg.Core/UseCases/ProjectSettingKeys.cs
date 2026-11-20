using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>Names of the project-scoped settings (ADR 0014) and the follow-up hints a client shows after changing them.</summary>
public static class ProjectSettingKeys
{
    public const string Prefix = "project.";
    public const string Service = "project.service";
    public const string Values = "project.values";
    public const string Secrets = "project.secrets";
    public const string Active = "project.active";
    public const string Ignore = "project.ignore";
    public const string VaultPrefix = "project.vault.";

    /// <summary>The value of <c>project.values</c> / <c>project.secrets</c> for <paramref name="target"/>: <c>&lt;target&gt;:&lt;project path&gt;</c>.</summary>
    public static string TargetValue(LocalTargetBinding target) =>
        $"{ProjectLookup.KindNames.First(k => k.Kind == target.Kind).Name}:{target.ProjectPath}";

    /// <summary><c>true</c> for any key in the <c>project.</c> namespace, whether known or not.</summary>
    public static bool IsProjectKey(string key) => key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What the developer should do after changing <paramref name="key"/>, or <c>null</c> when nothing: a new service or local target
    /// holds no data yet (nothing is moved), so the next pull fills it.
    /// </summary>
    public static string? Hint(string key)
    {
        if (key.Equals(Service, StringComparison.OrdinalIgnoreCase))
        {
            return "The active environment was cleared. Run 'azcfg use <ENV>' (or 'azcfg pull --force') to pull the new service before 'azcfg push': until then the local files still hold the previous service's keys.";
        }

        return key.Equals(Values, StringComparison.OrdinalIgnoreCase) || key.Equals(Secrets, StringComparison.OrdinalIgnoreCase)
            ? "Existing local data is not moved. Run 'azcfg pull --force' to fill the new target."
            : null;
    }
}
