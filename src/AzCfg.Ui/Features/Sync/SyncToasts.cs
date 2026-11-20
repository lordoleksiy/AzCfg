using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Sync;

public static class SyncToasts
{
    /// <summary>A toast with the lifetimes of §2.9: success/info 5 s, warning 8 s, error until closed.</summary>
    public static Task ToastAsync(this INotificationService notifications, string title, ToastIntent intent) =>
        notifications.ShowToastAsync(o =>
        {
            o.Title = title;
            o.Intent = intent;
            o.Lifetime = intent switch
            {
                ToastIntent.Error => TimeSpan.Zero,
                ToastIntent.Warning => TimeSpan.FromSeconds(8),
                _ => TimeSpan.FromSeconds(5),
            };
        });
}
