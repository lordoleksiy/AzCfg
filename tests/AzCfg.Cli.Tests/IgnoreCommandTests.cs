using AzCfg.Cli.Commands;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Tests;

public sealed class IgnoreCommandTests
{
    private const string Hint = "Local values of matching keys are kept; pull and push skip them. Nothing was deleted.";

    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/r/Orders.csproj", LocalFileFormat.NestedJson);

    private sealed class Stubs
    {
        public ProjectBinding? Existing { get; init; } = Binding(["global:*"]);
        public List<(string Op, string RepoPath, string Pattern)> Calls { get; } = [];
    }

    private sealed class StubProjects(Stubs s) : IProjectService
    {
        public Task<ProjectResolution> ResolveAsync(string repoPath, CancellationToken ct) => Task.FromResult(new ProjectResolution([], true, [], s.Existing));

        public Task<ProjectBinding> BindAsync(string repoPath, string service, LocalTargetBinding valuesTarget, LocalTargetBinding secretsTarget, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ProjectBinding> AddIgnoreAsync(string repoPath, string pattern, CancellationToken ct)
        {
            s.Calls.Add(("add", repoPath, pattern));
            return Task.FromResult(s.Existing! with { Ignore = [.. s.Existing.Ignore ?? [], pattern] });
        }

        public Task<ProjectBinding> RemoveIgnoreAsync(string repoPath, string pattern, CancellationToken ct)
        {
            s.Calls.Add(("remove", repoPath, pattern));
            return Task.FromResult(s.Existing! with { Ignore = [.. (s.Existing.Ignore ?? []).Where(i => i != pattern)] });
        }
    }

    private sealed class StubState(Stubs s) : IUserStateStore
    {
        public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => Task.FromResult(s.Existing);
        public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task SaveProfileAsync(UserProfile profile, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSnapshotsAsync(string repoPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static ProjectBinding Binding(IReadOnlyList<string>? ignore) => new("/r", "orders", Target, Target, new Dictionary<string, Uri>(), null, ignore);

    private static async Task<(int Exit, string Output)> Run(Stubs s, params string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(writer), Enrichment = new() { UseDefaultEnrichers = false } });
        console.Profile.Width = 200;
        var services = new ServiceCollection().AddSingleton<IProjectService>(new StubProjects(s)).AddSingleton<IUserStateStore>(new StubState(s));
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c =>
        {
            c.PropagateExceptions();
            c.ConfigureConsole(console);
            c.AddBranch("ignore", ignore =>
            {
                ignore.AddCommand<IgnoreAddCommand>("add");
                ignore.AddCommand<IgnoreRemoveCommand>("remove");
                ignore.AddCommand<IgnoreListCommand>("list");
            });
        });
        var exit = await app.RunAsync(["ignore", .. args]);
        return (exit, writer.ToString());
    }

    private static string[] Lines(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task Add_and_remove_pass_the_full_project_path_and_pattern(string op)
    {
        var s = new Stubs();

        await Run(s, op, "Logging:*", "--project", "repo");

        Assert.Equal((op, Path.GetFullPath("repo"), "Logging:*"), Assert.Single(s.Calls));
    }

    [Fact]
    public async Task Add_prints_the_resulting_patterns_and_the_hint()
    {
        var (exit, output) = await Run(new Stubs(), "add", "Logging:*");

        Assert.Equal(0, exit);
        Assert.Equal(["global:*", "Logging:*", Hint], Lines(output));
    }

    [Theory]
    [InlineData("global:*", "No ignored keys.")]
    [InlineData("Logging:*", "global:*")]
    public async Task Remove_prints_the_remaining_patterns_without_the_hint(string pattern, string expected)
    {
        var (exit, output) = await Run(new Stubs(), "remove", pattern);

        Assert.Equal(0, exit);
        Assert.Equal([expected], Lines(output));
    }

    public static TheoryData<string[]?, string[]> Listed => new()
    {
        { null, ["No ignored keys."] },
        { [], ["No ignored keys."] },
        { ["global:*", "*:Password"], ["global:*", "*:Password"] },
    };

    [Theory]
    [MemberData(nameof(Listed))]
    public async Task List_prints_one_pattern_per_line_or_none(string[]? ignore, string[] expected)
    {
        var s = new Stubs { Existing = Binding(ignore) };

        var (exit, output) = await Run(s, "list");

        Assert.Equal(0, exit);
        Assert.Equal(expected, Lines(output));
        Assert.Empty(s.Calls);
    }

    public static TheoryData<string[], string> Json => new()
    {
        { ["add", "Logging:*", "--json"], """{"ignore":["global:*","Logging:*"]}""" },
        { ["remove", "global:*", "--json"], """{"ignore":[]}""" },
        { ["list", "--json"], """{"ignore":["global:*"]}""" },
    };

    [Theory]
    [MemberData(nameof(Json))]
    public async Task Json_prints_only_the_ignore_list(string[] args, string expected)
    {
        var (exit, output) = await Run(new Stubs(), args);

        Assert.Equal(0, exit);
        Assert.Equal(expected, output.Trim());
    }

    [Fact]
    public async Task Json_list_of_a_binding_without_patterns_is_empty()
    {
        var (_, output) = await Run(new Stubs { Existing = Binding(null) }, "list", "--json");

        Assert.Equal("""{"ignore":[]}""", output.Trim());
    }

    [Fact]
    public async Task List_on_an_unbound_project_is_an_error()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(new Stubs { Existing = null }, "list"));

        Assert.Equal("This project is not bound yet. Run 'azcfg pull' first.", ex.Message);
    }
}
