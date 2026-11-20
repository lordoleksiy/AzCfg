using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Shared;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Environments;

/// <summary><c>env.setup</c> opens the environment wizard (FR-E1); <c>env.manage</c> opens the Settings tab (FR-E2).</summary>
public sealed class EnvironmentCommands(IDialogService dialogs, WorkbenchNavigator navigator) : ICommandContributor
{
    private const string Category = "Environments";

    public IEnumerable<UiCommand> Commands =>
    [
        new(CommandIds.EnvSetup, "Set up environments…", Category, KeyBindings.ShortcutOf(CommandIds.EnvSetup), SetupAsync),
        new(CommandIds.EnvManage, "Manage environments", Category, KeyBindings.ShortcutOf(CommandIds.EnvManage), () =>
        {
            navigator.OpenSettings();
            return Task.CompletedTask;
        }),
    ];

    private async Task SetupAsync() =>
        await dialogs.ShowDialogAsync<EnvironmentSetupWizard>(o =>
        {
            o.Header.Title = "Set up environments";
            o.Width = "780px";
            o.PreventDismissOnEscape = true;
        });
}
