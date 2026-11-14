using System.Diagnostics;

namespace AzCfg.Cli;

/// <summary>Runs <c>$VISUAL</c>, else <c>$EDITOR</c> (may carry arguments, e.g. <c>code --wait</c>), else <c>notepad</c> on Windows or <c>vi</c>.</summary>
public sealed class ProcessEditorLauncher : IEditorLauncher
{
    public async Task EditAsync(string path, CancellationToken ct)
    {
        var editor = new[] { "VISUAL", "EDITOR" }.Select(Environment.GetEnvironmentVariable).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
            ?? (OperatingSystem.IsWindows() ? "notepad" : "vi");
        var split = editor.Trim().Split(' ', 2);
        var info = new ProcessStartInfo(split[0]) { UseShellExecute = false };
        if (split.Length > 1)
        {
            info.ArgumentList.Add(split[1]);
        }

        info.ArgumentList.Add(path);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start editor '{split[0]}'.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Editor '{split[0]}' exited with code {process.ExitCode}.");
        }
    }
}
