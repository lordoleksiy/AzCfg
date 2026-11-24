using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// Lists the git-tracked <c>appsettings*.json</c> files under the repository (<c>git ls-files -z</c>) and flattens their keys to
/// <c>A:B</c> (array items by index, as .NET configuration does). Values are inspected in memory only to compute
/// <see cref="TrackedSettingKey.HasValue"/>: a non-empty string that is not a placeholder (<c>#{X}#</c>, <c>${X}</c>,
/// <c>__X__</c>, <c>{X}</c>). Outside a git repository (or without git, or when git fails or times out) the result is empty;
/// an unreadable or invalid file is skipped.
/// </summary>
public sealed class GitTrackedSettingsReader(ILogger<GitTrackedSettingsReader>? logger = null) : ITrackedSettingsReader
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private readonly ILogger _log = logger ?? NullLogger<GitTrackedSettingsReader>.Instance;

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
                    Flatten(file, Join(prefix, index++.ToString(CultureInfo.InvariantCulture)), item, keys);
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

    /// <summary>Only a non-empty, non-placeholder string can hold a secret; numbers and booleans are settings.</summary>
    private static bool HasValue(JsonElement element) =>
        element.ValueKind == JsonValueKind.String && element.GetString() is { } value && !string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value.Trim());

    private static bool IsPlaceholder(string value) =>
        (value.StartsWith("#{", StringComparison.Ordinal) && value.EndsWith("}#", StringComparison.Ordinal))
        || (value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}'))
        || (value.Length > 4 && value.StartsWith("__", StringComparison.Ordinal) && value.EndsWith("__", StringComparison.Ordinal))
        || (value.StartsWith('{') && value.EndsWith('}'));

    /// <summary>Paths relative to <paramref name="repoPath"/> with <c>/</c> separators; empty outside git or when git fails.</summary>
    private async Task<IReadOnlyList<string>> ListTrackedAsync(string repoPath, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(GitTimeout);
        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
            };
            foreach (var argument in (string[])["-c", "core.quotepath=false", "ls-files", "-z", "--", "*.json"])
            {
                startInfo.ArgumentList.Add(argument);
            }

            process = new Process { StartInfo = startInfo };
            process.Start();
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var files = await output;
            var stderr = await error;
            if (process.ExitCode == 0)
            {
                return files.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            }

            if (!stderr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogDebug("git ls-files exited with {ExitCode}; tracked settings check skipped", process.ExitCode);
            }

            return [];
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            _log.LogDebug("git ls-files timed out after {Timeout}; tracked settings check skipped", GitTimeout);
            return [];
        }
        catch (Exception ex)
        {
            // git missing, or the folder does not exist: the check is skipped.
            _log.LogDebug("git ls-files could not run ({ExceptionType}); tracked settings check skipped", ex.GetType().Name);
            return [];
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void Kill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already exited or never started.
        }
    }
}
