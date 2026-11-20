using System.Diagnostics;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// Git checks for local files inside the repository (ADR 0006, ADR 0009): a file is safe for secrets only when it is
/// git-ignored (<c>git check-ignore -q</c>) and not already tracked (<c>git ls-files --error-unmatch</c> fails); outside a
/// git repository it is always unsafe. <see cref="EnsureIgnoredAsync"/> adds the file name to the <c>.gitignore</c> next to it.
/// </summary>
internal static class GitIgnoreGuard
{
    public static async Task<bool> IsSafeForSecretsAsync(string path, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null)
        {
            return false;
        }

        var ignored = await RunGitAsync(directory, ["check-ignore", "-q", fullPath], ct);
        if (ignored != 0)
        {
            return false;
        }

        var tracked = await RunGitAsync(directory, ["ls-files", "--error-unmatch", fullPath], ct);
        return tracked != 0;
    }

    public static async Task EnsureIgnoredAsync(string path, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        if (await RunGitAsync(directory, ["check-ignore", "-q", fullPath], ct) == 0)
        {
            return;
        }

        var fileName = Path.GetFileName(fullPath);
        var gitIgnore = Path.Combine(directory, ".gitignore");
        var existing = File.Exists(gitIgnore) ? await File.ReadAllTextAsync(gitIgnore, ct) : "";
        if (existing.Split('\n').Select(l => l.Trim()).Any(l => l == fileName || l == "/" + fileName))
        {
            return;
        }

        var prefix = existing.Length == 0 || existing.EndsWith('\n') ? "" : Environment.NewLine;
        await File.AppendAllTextAsync(gitIgnore, prefix + fileName + Environment.NewLine, ct);
    }

    private static async Task<int> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // git missing, or not a repository (outside a git repository it is always unsafe).
            return -1;
        }
    }
}
