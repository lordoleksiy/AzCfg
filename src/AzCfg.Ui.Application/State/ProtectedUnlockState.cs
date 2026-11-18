namespace AzCfg.Ui.Application.State;

/// <summary>Protected environments unlocked for editing in this circuit (AD-7); keyed by environment name, OrdinalIgnoreCase.</summary>
public sealed class ProtectedUnlockState
{
    private readonly HashSet<string> _unlocked = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public bool IsUnlocked(string environment) => _unlocked.Contains(environment);

    public void Unlock(string environment)
    {
        if (_unlocked.Add(environment))
        {
            Changed?.Invoke();
        }
    }

    public void Lock(string environment)
    {
        if (_unlocked.Remove(environment))
        {
            Changed?.Invoke();
        }
    }
}
