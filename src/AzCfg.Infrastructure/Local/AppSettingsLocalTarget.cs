using System.Diagnostics;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// <see cref="LocalTargetKind.AppSettingsLocal"/>: <c>appsettings.local.json</c> next to the project's
/// <c>.csproj</c> (<see cref="LocalTargetBinding.ProjectPath"/>), nested JSON (ADR 0006). Any project can
/// use it, so <see cref="CanHandle"/> always returns <c>true</c>. A secrets target must live outside git's
/// reach: <see cref="InspectAsync"/> reports it safe only when the file is git-ignored
/// (<c>git check-ignore -q</c>) and not already tracked (<c>git ls-files --error-unmatch</c> fails);
/// outside a git repository it is always unsafe.
/// </summary>
public sealed class AppSettingsLocalTarget : JsonFileTarget
{
    private const string FileName = "appsettings.local.json";

    public override LocalTargetKind Kind => LocalTargetKind.AppSettingsLocal;

    public override bool CanHandle(ProjectInfo project) => true;

    public override async Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = GetFilePath(target);
        var exists = File.Exists(path);
        var isSafeForSecrets = await IsSafeForSecretsAsync(path, ct);

        return new LocalTargetOption(target, exists, isSafeForSecrets);
    }

    protected override string GetFilePath(LocalTargetBinding target)
    {
        var directory = Path.GetDirectoryName(target.ProjectPath);
        return string.IsNullOrEmpty(directory) ? FileName : Path.Combine(directory, FileName);
    }

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
