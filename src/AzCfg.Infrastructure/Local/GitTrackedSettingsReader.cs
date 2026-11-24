using System.Diagnostics;
using System.Text.Json;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// Lists the git-tracked <c>appsettings*.json</c> files under the repository (<c>git ls-files -z</c>) and flattens their keys to
/// <c>A:B</c> (array items by index, as .NET configuration does). Values are inspected in memory only to compute
/// <see cref="TrackedSettingKey.HasValue"/>. Outside a git repository (or without git) the result is empty; an unreadable or
/// invalid file is skipped.
/// </summary>
public sealed class GitTrackedSettingsReader : ITrackedSettingsReader
{
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public async Task<IReadOnlyList<TrackedSettingKey>> ReadKeysAsync(string repoPath, CancellationToken ct)
    {
        var files = await ListTrackedAsync(repoPath, ct);
        List<TrackedSettingKey> keys = [];
        foreach (var file in files.Where(IsAppSettings))
        {
            var path = Path.Combine(repoPath, file);
            try
            {
                await using var stream = File.OpenRead(path);
                using var document = await JsonDocument.ParseAsync(stream, Options, ct);
                Flatten(file, null, document.RootElement, keys);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Deleted from the working tree, locked or not valid JSON: nothing to report for this file.
            }
        }

        return keys;
    }

    private static bool IsAppSettings(string file)
    {
        var name = file[(file.LastIndexOf('/') + 1)..];
        return name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static void Flatten(string file, string? prefix, JsonElement element, List<TrackedSettingKey> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(file, Join(prefix, property.Name), property.Value, keys);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(file, Join(prefix, index++.ToString(System.Globalization.CultureInfo.InvariantCulture)), item, keys);
                }

                break;
            default:
                if (prefix is not null)
                {
                    keys.Add(new(file, prefix, HasValue(element)));
                }

                break;
        }
    }

    private static string Join(string? prefix, string segment) => prefix is null ? segment : $"{prefix}:{segment}";

    private static bool HasValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.String => element.GetString() is { Length: > 0 },
        _ => true,
    };

    /// <summary>Paths relative to <paramref name="repoPath"/> with <c>/</c> separators; empty outside git.</summary>
    private static async Task<IReadOnlyList<string>> ListTrackedAsync(string repoPath, CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in (string[])["ls-files", "-z", "--", "*.json"])
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var files = await output;
            await error;
            return process.ExitCode == 0 ? files.Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // git missing, or the folder does not exist: the check is skipped.
            return [];
        }
    }
}
