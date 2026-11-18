using AzCfg.Core.Model;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Shared;

/// <summary>Default <see cref="IActivationFlow"/> until U4 replaces it: shows "not available yet" and returns <c>false</c>.</summary>
public sealed class NotAvailableActivationFlow(INotificationService notifications) : IActivationFlow
{
    public async Task<bool> MakeActiveAsync(string projectId, ConfigEnvironment environment)
    {
        await notifications.ShowToastAsync(o =>
        {
            o.Title = "Make active is not available yet";
            o.Intent = ToastIntent.Info;
        });
        return false;
    }
}
