namespace AzCfg.Core.UseCases;

/// <summary>
/// Reads and changes the settings of one bound project (ADR 0014), as <see cref="UserSetting"/>s so that <c>config --json</c> keeps one shape.
/// Keys (case-insensitive): <c>project.service</c>, <c>project.values</c> and <c>project.secrets</c> (<c>&lt;target&gt;[:&lt;project path&gt;]</c>,
/// target = <c>user-secrets</c> | <c>appsettings-local</c> | <c>local-settings</c>), <c>project.vault.&lt;env&gt;</c> (Key Vault URI, empty = unset),
/// and the read-only <c>project.active</c> and <c>project.ignore</c>. A project that is not bound yet throws <see cref="InvalidOperationException"/>
/// from <see cref="GetAsync"/> and <see cref="SetAsync"/>.
/// </summary>
public interface IProjectSettingsService
{
    /// <summary>All project settings of the bound project; empty when <paramref name="repoPath"/> is not bound.</summary>
    Task<IReadOnlyList<UserSetting>> ListAsync(string repoPath, CancellationToken ct);

    /// <summary>One setting; an unknown key throws <see cref="ArgumentException"/> listing the known keys.</summary>
    Task<UserSetting> GetAsync(string repoPath, string key, CancellationToken ct);

    /// <summary>
    /// Validates <paramref name="value"/>, saves the binding and returns the new setting. An unknown or read-only key, an invalid value or an
    /// unknown environment throws <see cref="ArgumentException"/>; a secrets target that is not safe for secrets throws
    /// <see cref="InvalidOperationException"/>. The same value is a no-op. Changing <c>project.service</c> clears the active environment and
    /// drops the project's snapshots; changing a target moves no data (see <see cref="ProjectSettingKeys.Hint"/>).
    /// </summary>
    Task<UserSetting> SetAsync(string repoPath, string key, string? value, CancellationToken ct);
}
