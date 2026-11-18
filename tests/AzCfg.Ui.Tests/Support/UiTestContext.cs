using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Support;

/// <summary>bUnit context with the Fluent UI services and loose JS interop (Fluent and Monaco call JS freely).</summary>
public class UiTestContext : BunitContext
{
    public UiTestContext()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
