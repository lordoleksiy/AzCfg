namespace AzCfg.Ui.Features.Platform;

/// <summary>
/// Sidebar and inspector visibility and widths of one circuit (§2.2, §2.11). The viewport is only known to CSS, so each
/// pane has two flags: <c>…Visible</c> applies on wide viewports (sidebar ≥ 1024 px, inspector ≥ 1280 px), <c>…OverlayOpen</c>
/// on narrower ones where the pane is a drawer that starts closed. A toggle flips both, so it does the expected thing at any width.
/// </summary>
public sealed class LayoutState
{
    public const int SidebarMin = 200;
    public const int SidebarMax = 480;
    public const int InspectorMin = 260;
    public const int InspectorMax = 560;

    public bool SidebarVisible { get; private set; } = true;

    public bool SidebarOverlayOpen { get; private set; }

    public bool InspectorVisible { get; private set; } = true;

    public bool InspectorOverlayOpen { get; private set; }

    public int SidebarWidth { get; private set; } = 280;

    public int InspectorWidth { get; private set; } = 340;

    public event Action? Changed;

    public void ToggleSidebar()
    {
        SidebarVisible = !SidebarVisible;
        SidebarOverlayOpen = !SidebarOverlayOpen;
        Changed?.Invoke();
    }

    public void ToggleInspector()
    {
        InspectorVisible = !InspectorVisible;
        InspectorOverlayOpen = !InspectorOverlayOpen;
        Changed?.Invoke();
    }

    public void SetSidebarWidth(int width)
    {
        SidebarWidth = Math.Clamp(width, SidebarMin, SidebarMax);
        Changed?.Invoke();
    }

    public void SetInspectorWidth(int width)
    {
        InspectorWidth = Math.Clamp(width, InspectorMin, InspectorMax);
        Changed?.Invoke();
    }
}
