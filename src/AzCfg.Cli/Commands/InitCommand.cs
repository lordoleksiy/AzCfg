using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>
/// Chooses the environment mode (ADR 0008) and saves the user's environments: one store per environment (discovered stores)
/// or one shared store with a label per environment (labels found in the store). <c>--json</c> only prints the discovered
/// candidates; <c>--yes</c> accepts every detected store, or every label of <c>--store</c> in labels mode.
/// </summary>
public sealed class InitCommand(ISetupService setup, IAnsiConsole console) : AsyncCommand<InitSettings>
{
    private const string NoLabel = "(no label)";

    public override async Task<int> ExecuteAsync(CommandContext context, InitSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(await setup.DiscoverEnvironmentsAsync(cancellationToken)));
            return 0;
        }

        var mode = EnvironmentModeNames.TryParse(settings.Mode, out var parsed) ? parsed
            : settings.Yes ? EnvironmentMode.StorePerEnvironment
            : AskMode();
        var profile = mode == EnvironmentMode.LabelPerEnvironment
            ? await InitLabelsAsync(settings, cancellationToken)
            : await InitStoresAsync(settings, cancellationToken);
        await setup.SaveProfileAsync(profile, cancellationToken);

        console.WriteLine($"Mode: {EnvironmentModeNames.Format(profile.Mode)} ({Describe(profile.Mode)})");
        var table = new Table().AddColumns("Environment", "Endpoint", "Label", "Protected");
        foreach (var e in profile.Environments)
        {
            var label = profile.Mode == EnvironmentMode.LabelPerEnvironment ? e.Label ?? e.Name : e.Label ?? profile.DefaultLabel ?? NoLabel;
            table.AddRow(Markup.Escape(e.Name), Markup.Escape(e.StoreEndpoint.Host), Markup.Escape(label), e.IsProtected ? "yes" : "no");
        }

        console.Write(table);
        return 0;
    }

    public static IReadOnlyList<ConfigEnvironment> ToEnvironments(IEnumerable<EnvironmentCandidate> candidates) =>
        [.. candidates.Where(c => c.Name is not null).Select(c => new ConfigEnvironment(c.Name!, c.Store.Endpoint, null, c.IsProtected))];

    /// <summary>Suggests an environment name for a label: Development → dev, Staging/Stage → stage, Production/Prod → prod, else the lower-cased label.</summary>
    public static string SuggestName(string label) => label.Trim().ToLowerInvariant() switch
    {
        "development" => "dev",
        "staging" or "stg" => "stage",
        "production" or "prd" => "prod",
        var name => name,
    };

    /// <summary>Whether <paramref name="value"/> is an absolute http(s) URL.</summary>
    public static bool IsHttpEndpoint(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string Describe(EnvironmentMode mode) =>
        mode == EnvironmentMode.LabelPerEnvironment ? "One store, a label per environment" : "One store per environment";

    private EnvironmentMode AskMode() => console.Prompt(new SelectionPrompt<EnvironmentMode>()
        .Title("How are environments separated in App Configuration?")
        .UseConverter(Describe)
        .AddChoices(EnvironmentMode.StorePerEnvironment, EnvironmentMode.LabelPerEnvironment));

    private async Task<UserProfile> InitStoresAsync(InitSettings settings, CancellationToken ct)
    {
        var candidates = await setup.DiscoverEnvironmentsAsync(ct);
        var environments = settings.Yes ? ToEnvironments(candidates) : AskStores(candidates);
        var label = settings.Label ?? (settings.Yes ? null : await AskDefaultLabelAsync(environments, ct));
        return new(environments, EnvironmentMode.StorePerEnvironment, string.IsNullOrWhiteSpace(label) ? null : label);
    }

    private async Task<UserProfile> InitLabelsAsync(InitSettings settings, CancellationToken ct)
    {
        if (settings.Yes && settings.Store is null)
            throw new InvalidOperationException("--mode labels with --yes requires --store <endpoint>.");

        var store = settings.Store is not null ? new Uri(settings.Store) : AskSharedStore(await setup.DiscoverEnvironmentsAsync(ct));
        var labels = await setup.ListLabelsAsync(store, ct);
        IReadOnlyList<ConfigEnvironment> environments = settings.Yes
            ? [.. labels.DistinctBy(SuggestName, StringComparer.OrdinalIgnoreCase).Select(l => new ConfigEnvironment(SuggestName(l), store, l, ConfigEnvironment.IsProtectedByName(SuggestName(l))))]
            : AskLabels(store, labels);
        return new(environments, EnvironmentMode.LabelPerEnvironment, string.IsNullOrWhiteSpace(settings.Label) ? null : settings.Label);
    }

    private List<ConfigEnvironment> AskStores(IReadOnlyList<EnvironmentCandidate> candidates)
    {
        var environments = new List<ConfigEnvironment>();
        var chosen = candidates.Count == 0
            ? []
            : console.Prompt(new MultiSelectionPrompt<EnvironmentCandidate>()
                .Title("Select App Configuration stores to use")
                .NotRequired()
                .UseConverter(c => Markup.Escape($"{c.Store.Name} ({c.Store.Endpoint.Host})"))
                .AddChoices(candidates));

        foreach (var c in chosen)
        {
            var name = AskName(c.Store.Name, c.Name);
            environments.Add(new(name, c.Store.Endpoint, null, AskProtected(name, c.Name == name ? c.IsProtected : ConfigEnvironment.IsProtectedByName(name))));
        }

        while (console.Confirm("Add a store endpoint manually?", candidates.Count == 0 && environments.Count == 0))
        {
            var endpoint = AskEndpoint();
            var name = AskName(endpoint.OriginalString, null);
            environments.Add(new(name, endpoint, null, AskProtected(name, ConfigEnvironment.IsProtectedByName(name))));
        }

        return environments;
    }

    /// <summary>Offers the labels used in the chosen stores (stores that fail to list are skipped); <c>null</c> when none or "(no label)" is chosen.</summary>
    private async Task<string?> AskDefaultLabelAsync(IReadOnlyList<ConfigEnvironment> environments, CancellationToken ct)
    {
        var labels = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var store in environments.Select(e => e.StoreEndpoint).Distinct())
        {
            try
            {
                labels.UnionWith(await setup.ListLabelsAsync(store, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A store we cannot read labels from simply offers none.
            }
        }

        if (labels.Count == 0)
            return null;

        var choice = console.Prompt(new SelectionPrompt<string>()
            .Title("Label for new keys (the chosen stores use labels)")
            .UseConverter(Markup.Escape)
            .AddChoices([NoLabel, .. labels]));
        return choice == NoLabel ? null : choice;
    }

    private Uri AskSharedStore(IReadOnlyList<EnvironmentCandidate> candidates)
    {
        if (candidates.Count == 0)
            return AskEndpoint();

        var index = console.Prompt(new SelectionPrompt<int>()
            .Title("Select the shared App Configuration store")
            .UseConverter(i => i < candidates.Count
                ? Markup.Escape($"{candidates[i].Store.Name} ({candidates[i].Store.Endpoint.Host})")
                : "Enter a store endpoint manually")
            .AddChoices(Enumerable.Range(0, candidates.Count + 1)));
        return index < candidates.Count ? candidates[index].Store.Endpoint : AskEndpoint();
    }

    private List<ConfigEnvironment> AskLabels(Uri store, IReadOnlyList<string> labels)
    {
        var chosen = labels.Count == 0
            ? []
            : console.Prompt(new MultiSelectionPrompt<string>()
                .Title("Select the labels that are environments")
                .NotRequired()
                .UseConverter(Markup.Escape)
                .AddChoices(labels));

        var environments = chosen.Select(l => AskLabelEnvironment(store, l)).ToList();
        while (console.Confirm("Add an environment label manually?", environments.Count == 0))
            environments.Add(AskLabelEnvironment(store, console.Prompt(new TextPrompt<string>("Label:"))));

        return environments;
    }

    private ConfigEnvironment AskLabelEnvironment(Uri store, string label)
    {
        var name = AskName($"label '{label}'", SuggestName(label));
        return new(name, store, label, AskProtected(name, ConfigEnvironment.IsProtectedByName(name)));
    }

    private Uri AskEndpoint() => new(console.Prompt(new TextPrompt<string>("Store endpoint (https://<name>.azconfig.io):")
        .Validate(s => IsHttpEndpoint(s) ? ValidationResult.Success() : ValidationResult.Error("Not a valid http(s) URL"))));

    private string AskName(string store, string? suggested)
    {
        var prompt = new TextPrompt<string>($"Environment name for {Markup.Escape(store)}:");
        return console.Prompt(suggested is null ? prompt : prompt.DefaultValue(suggested));
    }

    private bool AskProtected(string name, bool suggested) =>
        console.Confirm($"Is '{Markup.Escape(name)}' protected (apply requires typing its name)?", suggested);
}
