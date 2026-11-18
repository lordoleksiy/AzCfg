namespace AzCfg.Core.UseCases;

/// <summary>
/// Reads and changes the per-user global settings kept in the profile (ADR 0008): <c>mode</c> (<c>stores</c> | <c>labels</c>),
/// <c>defaultLabel</c>, <c>gitignoreLocalFiles</c> (<c>true</c> | <c>false</c>, ADR 0009) and per environment <c>env.&lt;name&gt;.endpoint</c>, <c>env.&lt;name&gt;.label</c>, <c>env.&lt;name&gt;.protected</c>.
/// Keys and environment names match case-insensitively. Without a profile every call throws <see cref="InvalidOperationException"/>.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>All settings: <c>mode</c>, <c>defaultLabel</c>, <c>gitignoreLocalFiles</c>, then each environment in profile order.</summary>
    Task<IReadOnlyList<UserSetting>> ListAsync(CancellationToken ct);

    /// <summary>One setting; an unknown key throws <see cref="ArgumentException"/> listing the known keys.</summary>
    Task<UserSetting> GetAsync(string key, CancellationToken ct);

    /// <summary>Validates <paramref name="value"/>, saves the profile and returns the new setting; an unknown key, environment or invalid value throws <see cref="ArgumentException"/>.</summary>
    Task<UserSetting> SetAsync(string key, string? value, CancellationToken ct);
}
