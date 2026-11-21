using AzCfg.Ui.Hosting;

namespace AzCfg.Ui;

/// <summary>Entry point for hosts that embed the UI (the <c>azcfg ui</c> command); <c>Program</c> stays for <c>dotnet run</c>.</summary>
public static class UiHost
{
    /// <summary>
    /// Builds the UI host and runs it until <paramref name="ct"/> is cancelled. The login URL (with the one-time token) is
    /// printed to the console by the host: as a bare URL, or as one JSON line when <see cref="UiHostOptions.Json"/> is set.
    /// </summary>
    public static Task RunAsync(UiHostOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var app = AzCfgUiApp.Build(ToArgs(options));
        return app.RunAsync(ct);
    }

    /// <summary>The inverse of the configuration binding of <see cref="UiHostOptions"/>.</summary>
    public static string[] ToArgs(UiHostOptions options)
    {
        List<string> args = [$"--port={options.Port}", $"--no-browser={options.NoBrowser}", $"--json={options.Json}", $"--state-root={options.StateRoot}"];
        if (options.Demo is not null)
        {
            args.Add($"--demo={options.Demo}");
        }

        return [.. args];
    }
}
