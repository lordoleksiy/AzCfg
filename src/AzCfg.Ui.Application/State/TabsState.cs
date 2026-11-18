namespace AzCfg.Ui.Application.State;

/// <summary>
/// Workbench tabs of one circuit. At most <see cref="MaxLoaded"/> tabs stay loaded: activating a tab unloads the least
/// recently activated inactive, non-dirty tabs. Unknown ids are ignored.
/// </summary>
public sealed class TabsState
{
    public const int MaxLoaded = 8;

    private readonly List<WorkbenchTab> _tabs = [];
    private readonly List<Guid> _recent = [];

    public IReadOnlyList<WorkbenchTab> Tabs => _tabs;

    public Guid? ActiveId { get; private set; }

    public event Action? Changed;

    /// <summary>Opens and activates a tab; an existing tab with an equal descriptor is activated instead (title updated).</summary>
    public Guid Open(TabDescriptor descriptor, string title)
    {
        var index = _tabs.FindIndex(t => t.Descriptor == descriptor);
        Guid id;
        if (index >= 0)
        {
            id = _tabs[index].Id;
            _tabs[index] = _tabs[index] with { Title = title };
        }
        else
        {
            id = Guid.NewGuid();
            _tabs.Add(new WorkbenchTab(id, descriptor, title, false, false));
        }

        Activate(id);
        return id;
    }

    /// <summary>Closes a clean tab and returns <c>true</c>; a dirty tab stays and <c>false</c> asks for confirmation.</summary>
    public bool RequestClose(Guid id)
    {
        if (_tabs.Find(t => t.Id == id) is { IsDirty: true })
        {
            return false;
        }

        ForceClose(id);
        return true;
    }

    /// <summary>Closes the tab; when it was active, the tab now at its position (or the previous one) becomes active.</summary>
    public void ForceClose(Guid id)
    {
        var index = _tabs.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            return;
        }

        _tabs.RemoveAt(index);
        _recent.Remove(id);
        if (ActiveId != id)
        {
            Changed?.Invoke();
            return;
        }

        ActiveId = null;
        if (_tabs.Count == 0)
        {
            Changed?.Invoke();
            return;
        }

        Activate(_tabs[Math.Min(index, _tabs.Count - 1)].Id);
    }

    public void SetDirty(Guid id, bool dirty) => Replace(id, t => t with { IsDirty = dirty });

    /// <summary>Activates and loads the tab; more than <see cref="MaxLoaded"/> loaded → unload the oldest inactive non-dirty.</summary>
    public void Activate(Guid id)
    {
        var index = _tabs.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            return;
        }

        ActiveId = id;
        _tabs[index] = _tabs[index] with { IsLoaded = true };
        _recent.Remove(id);
        _recent.Add(id);
        UnloadExcess();
        Changed?.Invoke();
    }

    private void UnloadExcess()
    {
        foreach (var candidate in _recent.ToList())
        {
            if (_tabs.Count(t => t.IsLoaded) <= MaxLoaded)
            {
                return;
            }

            var index = _tabs.FindIndex(t => t.Id == candidate);
            if (candidate != ActiveId && _tabs[index] is { IsLoaded: true, IsDirty: false })
            {
                _tabs[index] = _tabs[index] with { IsLoaded = false };
            }
        }
    }

    private void Replace(Guid id, Func<WorkbenchTab, WorkbenchTab> change)
    {
        var index = _tabs.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            return;
        }

        _tabs[index] = change(_tabs[index]);
        Changed?.Invoke();
    }
}
