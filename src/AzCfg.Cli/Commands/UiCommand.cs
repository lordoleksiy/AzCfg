using AzCfg.Ui;
using AzCfg.Ui.Hosting;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// <c>azcfg ui</c> (ADR 0016): a launcher, not a workflow command. Starts the local web UI on loopback, prints the login URL
/// (with <c>--json</c>: one line <c>{ "url": … }</c>) and keeps running until Ctrl+C.
/// </summary>
public class UiCommand : AsyncCommand<UiSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UiSettings settings, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            await RunHostAsync(settings.ToHostOptions(), stop.Token);
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>The host start, overridable so tests do not open a socket.</summary>
    protected virtual Task RunHostAsync(UiHostOptions options, CancellationToken cancellationToken) =>
        UiHost.RunAsync(options, cancellationToken);
}
