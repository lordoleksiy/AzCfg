using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Shared;

/// <summary>Default <see cref="IBindingFlow"/> until U1 replaces it: shows "not available yet" and returns <c>false</c>.</summary>
public sealed class NotAvailableBindingFlow(INotificationService notifications) : IBindingFlow
{
    public async Task<bool> BindAsync(string projectId)
    {
        await notifications.ShowToastAsync(o =>
        {
            o.Title = "Project binding is not available yet";
            o.Intent = ToastIntent.Info;
        });
        return false;
    }
}
