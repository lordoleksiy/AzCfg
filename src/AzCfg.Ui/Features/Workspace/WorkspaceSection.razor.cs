using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace AzCfg.Ui.Features.Workspace;

/// <summary>
/// WORKSPACE sidebar section (§2.3): the tree Solution → Project → config files. A bound project shows its active environment (or
/// "not active") and, once its view is loaded, the counters ↑ local ↓ remote ! conflicts; an unbound application project shows
/// "unbound"; libraries are dimmed. Clicking a bound project opens its tab; clicking an unbound application project starts the
/// bind wizard first (§2.8 step 3). <c>local.settings.json</c> is a bindable target like the other files. A project shared via
/// <c>azcfg.json</c> carries the badge "shared (azcfg.json)"; one whose manifest cannot be applied shows "azcfg.json error" (the
/// reason in its tooltip and the selection panel). The selection panel offers Share with team for a bound project not shared yet.
/// </summary>
public sealed partial class WorkspaceSection : IDisposable
{
    private const string LooseId = "loose";
    private const string SharedBadge = "shared (azcfg.json)";
    private const string ManifestErrorBadge = "azcfg.json error";
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);
    private readonly HashSet<ProjectSession> _hooked = [];
    private string? _root;
    private int _generation;

    [Inject] private WorkspaceState Workspace { get; set; } = default!;

    [Inject] private ProjectSessions Sessions { get; set; } = default!;

    [Inject] private TabsState Tabs { get; set; } = default!;

    [Inject] private WorkbenchNavigator Navigator { get; set; } = default!;

    [Inject] private ICommandRegistry Commands { get; set; } = default!;

    [Inject] private IBindingFlow Binding { get; set; } = default!;

    [Inject] private WorkspaceActions Actions { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private string RootName => Workspace.Tree is { } tree ? Path.GetFileName(Path.TrimEndingDirectorySeparator(tree.RootPath)) is { Length: > 0 } name ? name : tree.RootPath : "No folder open";

    private ProjectNode? SelectedNode => Workspace.SelectedProjectId is { } id ? Workspace.FindProject(id) : null;

    protected override void OnInitialized()
    {
        Workspace.Changed += OnChanged;
        Sessions.Changed += OnChanged;
        Prepare();
    }

    /// <summary>A newly opened folder starts with its solutions expanded; sessions of the tree's projects are watched for counters.</summary>
    private void Prepare()
    {
        if (Workspace.Tree is { } tree && tree.RootPath != _root)
        {
            _root = tree.RootPath;
            _open.Clear();
            foreach (var solution in tree.Solutions)
            {
                _open.Add($"sln:{solution.Solution.Path}");
            }

            _open.Add(LooseId);
            _generation++;
        }

        foreach (var node in AllProjects())
        {
            if (Sessions.TryGet(node.Id, out var session) && _hooked.Add(session))
            {
                session.Changed += OnChanged;
            }
        }
    }

    private IEnumerable<ProjectNode> AllProjects() =>
        Workspace.Tree is { } tree ? tree.Solutions.SelectMany(s => s.Projects).Concat(tree.LooseProjects) : [];

    private static string Key(string parent, ProjectNode node) => $"{parent}|{node.Id}";

    private bool IsOpen(string id) => _open.Contains(id);

    private void SetOpen(string id, bool open)
    {
        if (open)
        {
            _open.Add(id);
        }
        else
        {
            _open.Remove(id);
        }
    }

    private void CollapseAll()
    {
        _open.Clear();
        _generation++;
    }

    private string ProjectText(ProjectNode node)
    {
        if (node.Project.Project.IsLibrary)
        {
            return node.Project.Project.Name;
        }

        var badge = node.IsShared ? $" · {SharedBadge}" : node.ManifestError is not null ? $" · {ManifestErrorBadge}" : string.Empty;
        if (node.Binding is not { } binding)
        {
            return $"{node.Project.Project.Name} · unbound{badge}";
        }

        var counters = Counters(node.Id, binding);
        return $"{node.Project.Project.Name} · {binding.ActiveEnvironment ?? "no active env"}{(counters.Length > 0 ? $" {counters}" : string.Empty)}{badge}";
    }

    /// <summary>"↑1 ↓2 !1" from the session's summary or cached view of the active environment; empty when neither is loaded.</summary>
    private string Counters(string projectId, ProjectBinding binding)
    {
        if (binding.ActiveEnvironment is not { } active || !Sessions.TryGet(projectId, out var session))
        {
            return string.Empty;
        }

        if (session.Summary is { IsActive: true } summary && string.Equals(summary.Environment, active, StringComparison.OrdinalIgnoreCase))
        {
            return Format(summary.LocalChanges, summary.RemoteChanges, summary.Conflicts.Count);
        }

        if (session.View is not { IsRemoteOnly: false } view
            || !string.Equals(view.Environment.Name, active, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var outgoing = view.Rows.Count(r => r.Status is DiffKind.LocalAdded or DiffKind.LocalModified or DiffKind.LocalDeleted);
        var incoming = view.Rows.Count(r => r.Status is DiffKind.RemoteAdded or DiffKind.RemoteModified or DiffKind.RemoteDeleted);
        var conflicts = view.Rows.Count(r => r.Status == DiffKind.Conflict);
        return Format(outgoing, incoming, conflicts);
    }

    private static string Format(int outgoing, int incoming, int conflicts) =>
        string.Join(' ', new[] { outgoing > 0 ? $"↑{outgoing}" : null, incoming > 0 ? $"↓{incoming}" : null, conflicts > 0 ? $"!{conflicts}" : null }.Where(s => s is not null));

    private static string ProjectTip(ProjectNode node)
    {
        var tip = node.Project.Project.IsLibrary
            ? $"{node.Project.Project.CsprojPath} — library or test project: no local targets"
            : node.Binding is { } binding ? $"{node.Project.Project.CsprojPath} — service ‘{binding.Service}’" : $"{node.Project.Project.CsprojPath} — not bound to a service yet";
        return node.ManifestPath is { } manifest ? $"{tip}; shared via {manifest}"
            : node.ManifestError is { } error ? $"{tip}; {error}"
            : tip;
    }

    private static string FileText(ProjectNode node, ConfigFileInfo file)
    {
        var name = file.Kind == ConfigFileKind.UserSecrets ? "user-secrets" : Path.GetFileName(file.Path);
        var roles = new List<string>();
        if (node.Binding is { } binding && TargetKind(file.Kind) is { } kind)
        {
            if (binding.ValuesTarget.Kind == kind)
            {
                roles.Add("values");
            }

            if (binding.SecretsTarget.Kind == kind)
            {
                roles.Add("secrets");
            }
        }

        return $"{name}{(file.Exists ? string.Empty : " (not created)")}{(roles.Count > 0 ? $" · {string.Join(", ", roles)}" : string.Empty)}";
    }

    private static Icon FileIcon(ConfigFileInfo file) => file.Kind == ConfigFileKind.UserSecrets
        ? new Icons.Regular.Size16.Key()
        : new Icons.Regular.Size16.Document();

    private static LocalTargetKind? TargetKind(ConfigFileKind kind) => kind switch
    {
        ConfigFileKind.UserSecrets => LocalTargetKind.UserSecrets,
        ConfigFileKind.AppSettingsLocal => LocalTargetKind.AppSettingsLocal,
        ConfigFileKind.LocalSettings => LocalTargetKind.LocalSettings,
        _ => null,
    };

    private async Task OnProjectSelected(ProjectNode node, bool selected)
    {
        if (!selected)
        {
            return;
        }

        Workspace.SelectProject(node.Id);
        if (node.Project.Project.IsLibrary)
        {
            return;
        }

        if (node.Binding is null && !await Binding.BindAsync(node.Id))
        {
            return;
        }

        Navigator.OpenProject(node.Id);
    }

    private async Task CopyPathAsync(ProjectNode node) =>
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", Path.GetDirectoryName(node.Project.Project.CsprojPath) ?? node.Project.Project.CsprojPath);

    private void OnChanged() => InvokeAsync(() =>
    {
        Prepare();
        StateHasChanged();
    });

    public void Dispose()
    {
        Workspace.Changed -= OnChanged;
        Sessions.Changed -= OnChanged;
        foreach (var session in _hooked)
        {
            session.Changed -= OnChanged;
        }
    }
}
