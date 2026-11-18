using AzCfg.Ui.Application.State;
using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Platform;

/// <summary>
/// What the palette lists for an input (§2.10): commands (empty or <c>&gt;</c>), projects (<c>#</c>), keys of the active project
/// tab (<c>@</c>), environments (<c>env:</c>). Quick open lists projects and keys without a prefix. Secret values are never
/// listed: keys are names only.
/// </summary>
public sealed class PaletteSearch(ICommandRegistry commands, WorkspaceState workspace, ProjectSessions sessions, TabsState tabs, WorkbenchNavigator navigator)
{
    public const int MaxItems = 50;

    public IReadOnlyList<PaletteItem> Search(string input, PaletteMode mode)
    {
        var text = input.TrimStart();
        return [.. (text switch
        {
            _ when text.StartsWith('>') => Commands(text[1..]),
            _ when text.StartsWith('#') => Projects(text[1..]),
            _ when text.StartsWith('@') => Keys(text[1..]),
            _ when text.StartsWith("env:", StringComparison.OrdinalIgnoreCase) => Environments(text[4..]),
            _ when mode == PaletteMode.QuickOpen => Projects(text).Concat(Keys(text)),
            _ => Commands(text),
        }).Take(MaxItems)];
    }

    private IEnumerable<PaletteItem> Commands(string query) =>
        FuzzyMatcher.Rank(commands.All, query.Trim(), c => c.Title)
            .Select(c => new PaletteItem(c.Title, c.Category, c.Shortcut, c.IsEnabled?.Invoke() != false, () => commands.ExecuteAsync(c.Id)));

    private IEnumerable<PaletteItem> Projects(string query)
    {
        var tree = workspace.Tree;
        var nodes = tree is null ? [] : tree.Solutions.SelectMany(s => s.Projects).Concat(tree.LooseProjects).DistinctBy(p => p.Id);
        return FuzzyMatcher.Rank(nodes, query.Trim(), p => p.Project.Project.Name)
            .Select(p => new PaletteItem(p.Project.Project.Name, p.Project.Project.CsprojPath, null, true, Run(() => navigator.OpenProject(p.Id))));
    }

    private IEnumerable<PaletteItem> Keys(string query)
    {
        if (ActiveSession() is not { View: { } view } session)
        {
            return [];
        }

        return FuzzyMatcher.Rank(view.Rows, query.Trim(), r => r.Key)
            .Select(r => new PaletteItem(r.Key, r.IsSecret ? "secret" : null, null, true, Run(() => navigator.OpenProject(session.ProjectId, mode: ProjectMode.Browse, key: r.Key))));
    }

    private IEnumerable<PaletteItem> Environments(string query)
    {
        var session = ActiveSession();
        var environments = workspace.Environments?.Environments ?? [];
        return FuzzyMatcher.Rank(environments, query.Trim(), e => e.Name)
            .Select(e => new PaletteItem(
                e.Name,
                e.IsProtected ? "protected" : null,
                null,
                session is not null,
                Run(() => session?.SetEnvironment(e.Name))));
    }

    private ProjectSession? ActiveSession() =>
        tabs.Tabs.FirstOrDefault(t => t.Id == tabs.ActiveId)?.Descriptor.ProjectId is { } id && sessions.TryGet(id, out var session) ? session : null;

    private static Func<Task> Run(Action action) => () =>
    {
        action();
        return Task.CompletedTask;
    };
}
