using System.Text.RegularExpressions;

namespace AzCfg.Ui.Tests.Components.Layout;

/// <summary>Source-level rules of the UI: colours only in app.css, and the component guide covers the Fluent APIs in use.</summary>
public sealed partial class UiSourceRulesTests
{
    private static readonly string Root = FindRoot();
    private static readonly string UiProject = Path.Combine(Root, "src", "AzCfg.Ui");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AzCfg.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("AzCfg.slnx not found above the test binaries.");
    }

    private static IEnumerable<string> Files(string pattern, params string[] folders) =>
        folders.SelectMany(f => Directory.EnumerateFiles(Path.Combine(UiProject, f), pattern, SearchOption.AllDirectories));

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"<(Fluent[A-Za-z]+)")]
    private static partial Regex FluentTag();

    [GeneratedRegex(@"\b(IDialogService|INotificationService|IThemeService|IKeyCodeService)\b")]
    private static partial Regex FluentService();

    [Fact]
    public void RazorFiles_ContainNoHexColours()
    {
        var offenders = Directory.EnumerateFiles(UiProject, "*.razor*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => HexColour().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(Root, f))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void AppCss_DefinesTheTokensOfSection212()
    {
        var css = File.ReadAllText(Path.Combine(UiProject, "wwwroot", "css", "app.css"));
        foreach (var token in new[]
        {
            "--az-env-dev: #3fb68b", "--az-env-stage: #e5a43b", "--az-env-prod: #f0605d", "--az-env-custom: #a78bfa",
            "--az-diff-added: #3fb68b", "--az-diff-modified: #e5a43b", "--az-diff-deleted: #f0605d",
            "--az-diff-conflict: #f472b6", "--az-diff-remote: #5aa9e6", "--az-draft: #5b9dff",
            "--az-row-h: 28px", "--az-header-h: 36px", "--az-tab-h: 32px", "--az-statusbar-h: 24px",
            "--az-activitybar-w: 48px", "--az-sidebar-w: 280px", "--az-inspector-w: 340px",
        })
        {
            Assert.Contains(token, css);
        }
    }

    [Fact]
    public void ComponentGuide_CoversEveryFluentApiUsedByThePlatform()
    {
        var guide = File.ReadAllText(Path.Combine(Root, "docs", "ui-components.md"));
        var sources = Files("*.razor", "Components/Layout", "Components/Common", "Components/KeyTree")
            .Concat(Files("*.cs", "Components/Layout", "Components/Common", "Components/KeyTree", "Features/Platform"))
            .Select(File.ReadAllText)
            .ToList();
        var used = sources.SelectMany(s => FluentTag().Matches(s).Select(m => m.Groups[1].Value))
            .Concat(sources.SelectMany(s => FluentService().Matches(s).Select(m => m.Value)))
            .Distinct()
            .ToList();
        Assert.NotEmpty(used);
        var missing = used.Where(name => !guide.Contains(name, StringComparison.Ordinal)).ToList();
        Assert.Empty(missing);
    }
}
