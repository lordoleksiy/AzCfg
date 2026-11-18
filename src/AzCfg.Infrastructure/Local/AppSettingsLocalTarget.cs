using System.Diagnostics;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// <see cref="LocalTargetKind.AppSettingsLocal"/>: <c>appsettings.local.json</c> next to the project's
/// <c>.csproj</c> (<see cref="LocalTargetBinding.ProjectPath"/>), nested JSON (ADR 0006). Offered only for
/// application projects (not libraries) that already have any <c>appsettings*.json</c> (e.g. <c>appsettings.json</c>,
/// <c>appsettings.Development.json</c>, <c>appsettings.Local.json</c>; case-insensitive) next to the <c>.csproj</c>
/// (see <see cref="CanHandle"/>). An existing local file is matched case-insensitively and keeps its real name
/// (e.g. <c>appsettings.Local.json</c>); otherwise <c>appsettings.local.json</c> is used. A secrets target must live outside git's
/// reach: <see cref="InspectAsync"/> reports it safe only when the file is git-ignored
/// (<c>git check-ignore -q</c>) and not already tracked (<c>git ls-files --error-unmatch</c> fails);
/// outside a git repository it is always unsafe.
/// </summary>
public sealed class AppSettingsLocalTarget : JsonFileTarget
{
    private const string DefaultFileName = "appsettings.local.json";

    public override LocalTargetKind Kind => LocalTargetKind.AppSettingsLocal;

    public override bool CanHandle(ProjectInfo project) =>
        !project.IsLibrary
        && Path.GetDirectoryName(project.CsprojPath) is { } directory
        && Directory.Exists(directory)
        && Directory.EnumerateFiles(directory, "*.json")
            .Any(f => Path.GetFileName(f).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase));

    public override async Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = GetFilePath(target);
        var exists = File.Exists(path);
        var isSafeForSecrets = await IsSafeForSecretsAsync(path, ct);

        return new LocalTargetOption(target, exists, isSafeForSecrets);
    }

    public override async Task EnsureGitIgnoredAsync(LocalTargetBinding target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = Path.GetFullPath(GetFilePath(target));
        var directory = Path.GetDirectoryName(path)!;
        if (await RunGitAsync(directory, ["check-ignore", "-q", path], ct) == 0)
        {
            return;
        }

        var fileName = Path.GetFileName(path);
        var gitIgnore = Path.Combine(directory, ".gitignore");
        var existing = File.Exists(gitIgnore) ? await File.ReadAllTextAsync(gitIgnore, ct) : "";
        if (existing.Split('\n').Select(l => l.Trim()).Any(l => l == fileName || l == "/" + fileName))
        {
            return;
        }

        var prefix = existing.Length == 0 || existing.EndsWith('\n') ? "" : Environment.NewLine;
        await File.AppendAllTextAsync(gitIgnore, prefix + fileName + Environment.NewLine, ct);
    }

    protected override string GetFilePath(LocalTargetBinding target)
    {
        var directory = Path.GetDirectoryName(target.ProjectPath);
        var fileName = FindLocalFileName(string.IsNullOrEmpty(directory) ? "." : directory) ?? DefaultFileName;
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    private static string? FindLocalFileName(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json")
                .Select(Path.GetFileName)
                .FirstOrDefault(f => string.Equals(f, DefaultFileName, StringComparison.OrdinalIgnoreCase))
            : null;

    private static async Task<bool> IsSafeForSecretsAsync(string path, CancellationToken ct)
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
            // git missing, or not a repository (outside git-репо завжди небезпечно).
            return -1;
        }
    }
}
