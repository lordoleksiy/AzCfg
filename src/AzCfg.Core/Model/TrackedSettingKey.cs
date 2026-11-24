namespace AzCfg.Core.Model;

/// <summary>A key of a git-tracked settings file. Never carries the value.</summary>
/// <param name="File">The file path relative to the repository, as git lists it (<c>/</c> separators).</param>
/// <param name="Key">The flattened key path (<c>A:B</c>).</param>
/// <param name="HasValue"><c>true</c> when the value is not null and not an empty string.</param>
public sealed record TrackedSettingKey(string File, string Key, bool HasValue);
