using System.Text.Json.Serialization;

namespace AzCfg.Core.UseCases;

/// <summary>
/// One per-user global setting stored in the profile (ADR 0008), e.g. <c>mode</c> or <c>env.dev.label</c>, or a project setting
/// (ADR 0014); <see cref="Value"/> is <c>null</c> when unset. Project settings may add <see cref="Source"/> — the shared
/// <c>azcfg.json</c> the value is (also) kept in (ADR 0012 amendment 2) — and, on a change, <see cref="Hint"/>, what the developer
/// should do next. Both are omitted from JSON when <c>null</c>, so <c>config --json</c> keeps its shape.
/// </summary>
public sealed record UserSetting(string Key, string? Value)
{
    /// <summary>The full path of the shared manifest holding this value; <c>null</c> when the value is per user only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }

    /// <summary>A follow-up for the developer after a change (e.g. <see cref="ProjectSettingKeys.SharedHint"/>); <c>null</c> when none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; init; }
}
