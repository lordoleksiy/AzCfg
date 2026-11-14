using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Commands;

/// <summary>Discovers App Configuration stores and saves the chosen environments; <c>--json</c> only prints candidates, <c>--yes</c> accepts every detected one.</summary>
public sealed class InitCommand(ISetupService setup, IAnsiConsole console) : AsyncCommand<InitSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InitSettings settings, CancellationToken cancellationToken)
    {
        var candidates = await setup.DiscoverEnvironmentsAsync(cancellationToken);
        if (settings.Json)
        {
            console.Profile.Out.Writer.WriteLine(CliJson.Serialize(candidates));
            return 0;
        }

        var environments = settings.Yes ? ToEnvironments(candidates) : Ask(candidates);
        await setup.SaveProfileAsync(new(environments), cancellationToken);

        var table = new Table().AddColumns("Environment", "Endpoint", "Protected");
        foreach (var e in environments)
            table.AddRow(Markup.Escape(e.Name), Markup.Escape(e.StoreEndpoint.Host), e.IsProtected ? "yes" : "no");
        console.Write(table);
        return 0;
    }

    public static IReadOnlyList<ConfigEnvironment> ToEnvironments(IEnumerable<EnvironmentCandidate> candidates) =>
        [.. candidates.Where(c => c.Name is not null).Select(c => new ConfigEnvironment(c.Name!, c.Store.Endpoint, null, c.IsProtected))];

    private List<ConfigEnvironment> Ask(IReadOnlyList<EnvironmentCandidate> candidates)
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
            var endpoint = console.Prompt(new TextPrompt<string>("Store endpoint (https://<name>.azconfig.io):")
                .Validate(s => Uri.TryCreate(s, UriKind.Absolute, out _) ? ValidationResult.Success() : ValidationResult.Error("Not a valid URL")));
            var name = AskName(endpoint, null);
            environments.Add(new(name, new(endpoint), null, AskProtected(name, ConfigEnvironment.IsProtectedByName(name))));
        }

        return environments;
    }

    private string AskName(string store, string? suggested)
    {
        var prompt = new TextPrompt<string>($"Environment name for {Markup.Escape(store)}:");
        return console.Prompt(suggested is null ? prompt : prompt.DefaultValue(suggested));
    }

    private bool AskProtected(string name, bool suggested) =>
        console.Confirm($"Is '{Markup.Escape(name)}' protected (apply requires typing its name)?", suggested);
}
