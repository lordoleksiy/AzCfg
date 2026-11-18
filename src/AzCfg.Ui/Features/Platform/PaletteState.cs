namespace AzCfg.Ui.Features.Platform;

/// <summary>Whether the command palette is open and how it was opened (scoped).</summary>
public sealed class PaletteState
{
    public bool IsOpen { get; private set; }

    public PaletteMode Mode { get; private set; }

    public event Action? Changed;

    public void Open(PaletteMode mode)
    {
        Mode = mode;
        IsOpen = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (IsOpen)
        {
            IsOpen = false;
            Changed?.Invoke();
        }
    }
}
