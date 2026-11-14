using AzCfg.Cli.Commands;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using Spectre.Console;

namespace AzCfg.Cli.Tests.Commands;

public sealed class InitCommandTests
{
    private sealed class FakeSetup(params EnvironmentCandidate[] candidates) : ISetupService
    {
        public UserProfile? Saved;

        public Task<IReadOnlyList<EnvironmentCandidate>> DiscoverEnvironmentsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EnvironmentCandidate>>(candidates);

        public Task SaveProfileAsync(UserProfile profile, CancellationToken ct)
        {
            Saved = profile;
            return Task.CompletedTask;
        }
    }

    private static EnvironmentCandidate Candidate(string store, string? name, bool isProtected = false) => new(
        name, new($"/s/{store}", store, new($"https://{store}.azconfig.io"), "s", "rg", new Dictionary<string, string>()), isProtected);

    private static async Task<(int Code, string Output, FakeSetup Setup)> Run(InitSettings settings, params EnvironmentCandidate[] candidates)
    {
        var setup = new FakeSetup(candidates);
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(writer) });
        var code = await new InitCommand(setup, console).ExecuteAsync(null!, settings, CancellationToken.None);
        return (code, writer.ToString(), setup);
    }

    [Fact]
    public void ToEnvironments_MapsNamedCandidatesAndSkipsUndetermined()
    {
        var result = InitCommand.ToEnvironments([Candidate("a-dev", "dev"), Candidate("a-qa", null), Candidate("a-prod", "prod", true)]);

        Assert.Equal(
            [new ConfigEnvironment("dev", new("https://a-dev.azconfig.io"), null, false), new ConfigEnvironment("prod", new("https://a-prod.azconfig.io"), null, true)],
            result);
    }

    [Fact]
    public async Task Json_PrintsCandidatesAndDoesNotSave()
    {
        var (code, output, setup) = await Run(new InitSettings { Json = true }, Candidate("a-prod", "prod", true), Candidate("a-qa", null));

        Assert.Equal(0, code);
        Assert.Null(setup.Saved);
        Assert.Equal(
            """[{"name":"prod","store":{"id":"/s/a-prod","name":"a-prod","endpoint":"https://a-prod.azconfig.io","subscriptionId":"s","resourceGroup":"rg","tags":{}},"isProtected":true},{"name":null,"store":{"id":"/s/a-qa","name":"a-qa","endpoint":"https://a-qa.azconfig.io","subscriptionId":"s","resourceGroup":"rg","tags":{}},"isProtected":false}]""",
            output.Trim());
    }

    [Fact]
    public async Task Yes_SavesDetectedEnvironmentsWithoutPrompting()
    {
        var (code, _, setup) = await Run(new InitSettings { Yes = true }, Candidate("a-dev", "dev"), Candidate("a-qa", null), Candidate("a-prod", "prod", true));

        Assert.Equal(0, code);
        Assert.Equal([("dev", false), ("prod", true)], setup.Saved!.Environments.Select(e => (e.Name, e.IsProtected)));
    }
}
