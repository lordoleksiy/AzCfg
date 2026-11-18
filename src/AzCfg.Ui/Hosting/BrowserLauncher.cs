using System.ComponentModel;
using System.Diagnostics;

namespace AzCfg.Ui.Hosting;

/// <summary>Opens the login URL in the default browser. Virtual so tests can record instead of launching.</summary>
public class BrowserLauncher
{
    /// <summary>Returns false when no browser could be started; the URL is already in the console either way.</summary>
    public virtual bool TryOpen(string url)
    {
        try
        {
            using var process = Process.Start(CreateStartInfo(url));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string url) =>
        OperatingSystem.IsWindows() ? new ProcessStartInfo(url) { UseShellExecute = true }
        : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open", url)
        : new ProcessStartInfo("xdg-open", url);
}
