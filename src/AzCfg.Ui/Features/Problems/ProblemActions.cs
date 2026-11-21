using AzCfg.Core.Health;
using AzCfg.Ui.Application.Health;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Sync;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AzCfg.Ui.Features.Problems;

public enum ProblemAction
{
    /// <summary>Open Browse with the finding's key selected.</summary>
    OpenKey,

    /// <summary>Open the Settings tab (project and git-ignore settings).</summary>
    OpenSettings,

    /// <summary>Copy the CLI fix command to the clipboard.</summary>
    CopyFix,
}

/// <summary>The actions of a finding (FR-H1): a key opens Browse, target/binding findings open Settings, any other fix is copied.</summary>
public sealed class ProblemActions(WorkbenchNavigator navigator, IJSRuntime js, INotificationService notifications)
{
    /// <summary>The main action of <paramref name="row"/>; null when nothing can be done from the UI.</summary>
    public static ProblemAction? PrimaryOf(ProblemRow row) => row switch
    {
        { Key: { Length: > 0 } } => ProblemAction.OpenKey,
        { Kind: HealthCheckKind.ValuesTargetNotIgnored or HealthCheckKind.BindingMismatch } => ProblemAction.OpenSettings,
        { Fix: { Length: > 0 } } => ProblemAction.CopyFix,
        _ => null,
    };

    /// <summary>True when the row also offers its fix command next to a different main action.</summary>
    public static bool HasCopyFix(ProblemRow row) => row.Fix is { Length: > 0 } && PrimaryOf(row) != ProblemAction.CopyFix;

    public async Task RunAsync(string projectId, ProblemRow row, ProblemAction action)
    {
        switch (action)
        {
            case ProblemAction.OpenKey when row.Key is { } key:
                navigator.OpenProject(projectId, SingleEnvironment(row.Environment), ProjectMode.Browse, key);
                break;
            case ProblemAction.OpenSettings:
                navigator.OpenSettings();
                break;
            case ProblemAction.CopyFix when row.Fix is { } fix:
                await js.InvokeVoidAsync("navigator.clipboard.writeText", fix);
                await notifications.ToastAsync("Copied the fix command", ToastIntent.Info);
                break;
        }
    }

    /// <summary>A finding may name several environments ("stage, prod"); only a single name selects one, else the active one stays.</summary>
    private static string? SingleEnvironment(string? environment) =>
        environment is { Length: > 0 } && !environment.Contains(',') ? environment : null;
}
