using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Compares two sides: no args = local and the active environment, <c>&lt;env&gt;</c> = local and that environment,
/// <c>&lt;a&gt; &lt;b&gt;</c> = two environments (either may be <c>local</c>). <c>--missing --all</c> lists keys absent from some environments
/// and exits 1 when there are any. Plain values are shown, secrets only as same/different, never their values.
/// </summary>
public sealed class DiffCommand(IProjectService projects, ISyncService sync, IConfigComparisonService comparison, IUserStateStore state, IEnvironmentResolver resolver, IAnsiConsole console)
    : AsyncCommand<DiffSettings>
{
    private const string LocalWord = "local";

    public override async Task<int> ExecuteAsync(CommandContext context, DiffSettings settings, CancellationToken ct)
    {
        var profile = await state.LoadProfileAsync(ct) ?? throw new InvalidOperationException("No profile found. Run 'azcfg init' first.");
        var repo = Path.GetFullPath(settings.Project);
        var resolution = await projects.ResolveAsync(repo, ct);
        ManifestNoticeView.Render(console, resolution, settings.Json);
        var binding = resolution.Existing ?? throw new InvalidOperationException("This project is not bound yet. Run 'azcfg pull' first.");
        var environments = resolver.Resolve(profile);

        return settings.Missing ? await MissingAsync(binding, environments, settings, ct) : await CompareAsync(binding, environments, settings, ct);
    }

    private async Task<int> CompareAsync(ProjectBinding binding, IReadOnlyList<ConfigEnvironment> environments, DiffSettings settings, CancellationToken ct)
    {
        var (left, right) = (settings.Left, settings.Right) switch
        {
            (null, _) => (Local(), Side(environments, settings.Env ?? binding.ActiveEnvironment ?? "dev")),
            (var l, null) => (Local(), Side(environments, l)),
            (var l, var r) => (Side(environments, l), Side(environments, r)),
        };

        if (left is CompareSide.Local && right is CompareSide.Local)
        {
            throw new InvalidOperationException("Cannot compare local with local: name an environment (e.g. 'azcfg diff prod').");
        }

        var result = await comparison.CompareAsync(binding, left, right, ct);
        var shown = settings.Same ? result.Items : [.. result.Items.Where(i => i.Kind != ComparisonKind.Same)];
        var report = result with { Items = shown };

        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(report));
            return 0;
        }

        DiffView.Render(console, Name(left), Name(right), report, result.Items.Count(i => i.Kind == ComparisonKind.Same));
        return 0;
    }

    private async Task<int> MissingAsync(ProjectBinding binding, IReadOnlyList<ConfigEnvironment> environments, DiffSettings settings, CancellationToken ct)
    {
        var configs = new List<RemoteConfig>();
        foreach (var env in environments)
        {
            var config = await sync.ReadRemoteConfigAsync(binding, env, ct);
            configs.Add(config with { Entries = [.. config.Entries.Where(e => !IgnoreRules.IsIgnored(binding.Ignore, e.Key, e.Scope))] });
        }

        var missing = MissingKeysReport.Compute(configs);
        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(missing));
        }
        else
        {
            DiffView.RenderMissing(console, [.. environments.Select(e => e.Name)], missing);
        }

        return missing.Count == 0 ? 0 : 1;
    }

    private static CompareSide Local() => new CompareSide.Local();

    private static CompareSide Side(IReadOnlyList<ConfigEnvironment> environments, string name) =>
        name.Equals(LocalWord, StringComparison.OrdinalIgnoreCase)
            ? Local()
            : new CompareSide.Environment(environments.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Environment '{name}' is not in the profile."));

    private static string Name(CompareSide side) => side is CompareSide.Environment e ? e.Env.Name : LocalWord;
}
