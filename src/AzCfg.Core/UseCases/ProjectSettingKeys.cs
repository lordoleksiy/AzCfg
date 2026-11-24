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

    /// <summary>
    /// Whether the shared fields live in a git-tracked <c>azcfg.json</c> (ADR 0012 amendment 2): <c>true</c> (the setting's
    /// <see cref="UserSetting.Source"/> is the manifest path) or <c>false</c>. Settable only to <c>true</c>.
    /// </summary>
    public const string Shared = "project.shared";

    /// <summary>The <c>project.shared</c> value in <c>config list</c> when the manifest cannot be read (its path is the source).</summary>
    public const string SharedInvalid = "invalid";

    /// <summary>The <see cref="UserSetting.Hint"/> of a change written to the shared <c>azcfg.json</c>.</summary>
    public const string SharedHint = "Shared via azcfg.json — commit it.";

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
