using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Components.Common;

public static class DialogServiceExtensions
{
    /// <summary>A Yes/No confirmation; <c>true</c> when the primary button was chosen.</summary>
    public static async Task<bool> ConfirmAsync(this IDialogService dialogs, string title, string message, string primary = "OK", string secondary = "Cancel")
    {
        var result = await dialogs.ShowConfirmationAsync(message, title, primary, secondary);
        return !result.Cancelled;
    }

    /// <summary>
    /// Confirmation that needs <paramref name="expected"/> typed (case-insensitive) before the primary button is enabled;
    /// <c>Enter</c> never submits (AD-7). <c>true</c> when confirmed.
    /// </summary>
    public static async Task<bool> ConfirmTypedAsync(this IDialogService dialogs, string title, string message, string expected)
    {
        var result = await dialogs.ShowDialogAsync<TypedConfirmDialog>(o =>
        {
            o.Header.Title = title;
            o.Parameters.Add(nameof(TypedConfirmDialog.Message), message);
            o.Parameters.Add(nameof(TypedConfirmDialog.Expected), expected);
        });
        return !result.Cancelled;
    }
}
