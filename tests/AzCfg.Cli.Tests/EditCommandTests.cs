using AzCfg.Cli.Commands;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Tests;

public sealed class EditCommandTests
{
    private const string Exported = """{"Db":{"Host":"h"}}""";

    private static readonly LocalTargetBinding Target = new(LocalTargetKind.UserSecrets, "/r/Orders.csproj", LocalFileFormat.NestedJson);
    private static readonly ProjectBinding Binding = new("/r", "orders", Target, Target, new Dictionary<string, Uri>());

    private sealed class Stubs
    {
        public Func<string, string?> Edit { get; init; } = _ => """{"Db":{"Host":"edited"}}""";
        public Exception? EditorFails { get; init; }
        public List<string> EditorPaths { get; } = [];
        public List<string> ContentSeenByEditor { get; } = [];
        public List<(ConfigEnvironment Env, PushSource Source)> Planned { get; } = [];
        public List<SyncPlan> Applied { get; } = [];
    }

    private sealed class StubEditor(Stubs s) : IEditorLauncher
    {
        public async Task EditAsync(string path, CancellationToken ct)
        {
            s.EditorPaths.Add(path);
            var content = await File.ReadAllTextAsync(path, ct);
            s.ContentSeenByEditor.Add(content);
            if (s.EditorFails is { } ex)
            {
                throw ex;
            }

            if (s.Edit(content) is { } edited)
            {
                await File.WriteAllTextAsync(path, edited, ct);
            }
        }
    }

    private sealed class StubState : IUserStateStore
    {
        public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => Task.FromResult<UserProfile?>(new([new("dev", new Uri("https://dev.azconfig.io"), null, false)]));
        public Task SaveProfileAsync(UserProfile profile, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubProjects : IProjectService
    {
        public Task<ProjectResolution> ResolveAsync(string repoPath, CancellationToken ct) => Task.FromResult(new ProjectResolution([], true, [], Binding));
        public Task<ProjectBinding> BindAsync(string repoPath, string service, LocalTargetBinding valuesTarget, LocalTargetBinding secretsTarget, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectBinding> AddIgnoreAsync(string repoPath, string pattern, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectBinding> RemoveIgnoreAsync(string repoPath, string pattern, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubSync(Stubs s) : ISyncService
    {
        public Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => Task.FromResult(Exported);

        public Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct)
        {
            s.Planned.Add((env, source));
            return Task.FromResult(new SyncPlan(binding, env, [new(ChangeKind.Update, null, new("Db:Host", ConfigScope.Service, new ConfigValue.Plain("edited")))], [], false, false));
        }

        public Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct)
        {
            s.Applied.Add(plan);
            return Task.FromResult(new ApplyResult(1, []));
        }

        public Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, bool force, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteConfig> ReadRemoteConfigAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();
        public Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct) => throw new NotSupportedException();
        public Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictResolution resolution, CancellationToken ct) => throw new NotSupportedException();
    }

    private static async Task<(int Exit, string Output)> Run(Stubs s, params string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new() { Out = new AnsiConsoleOutput(writer), Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors });
        console.Profile.Width = 200;
        var services = new ServiceCollection()
            .AddSingleton<IUserStateStore>(new StubState())
            .AddSingleton<IProjectService>(new StubProjects())
            .AddSingleton<ISyncService>(new StubSync(s))
            .AddSingleton<IEditorLauncher>(new StubEditor(s))
            .AddSingleton<IResourceDiscovery>(new StubDiscovery())
            .AddSingleton<IEnvironmentResolver, SeparateStoresResolver>();
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(c =>
        {
            c.PropagateExceptions();
            c.ConfigureConsole(console);
            c.AddCommand<EditCommand>("edit");
        });
        var exit = await app.RunAsync(["edit", .. args]);
        return (exit, writer.ToString());
    }

    private sealed class StubDiscovery : IResourceDiscovery
    {
        public Task<IReadOnlyList<AzureResource>> FindAppConfigStoresAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AzureResource>> FindKeyVaultsAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Edited_file_is_planned_as_EditedJson_applied_and_temp_file_deleted()
    {
        var s = new Stubs();

        var (exit, output) = await Run(s, "--yes");

        var path = Assert.Single(s.EditorPaths);
        Assert.Equal(0, exit);
        Assert.Equal(Exported, Assert.Single(s.ContentSeenByEditor));
        Assert.Equal(".json", Path.GetExtension(path));
        Assert.StartsWith(Path.GetTempPath(), path);
        Assert.False(File.Exists(path));
        Assert.Equal("""{"Db":{"Host":"edited"}}""", Assert.IsType<PushSource.EditedJson>(Assert.Single(s.Planned).Source).Json);
        Assert.Single(s.Applied);
        Assert.Matches(@"Applied\s+1", output);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Exported)]
    public async Task Unchanged_file_reports_no_changes_without_planning(string? edited)
    {
        var s = new Stubs { Edit = _ => edited };

        var (exit, output) = await Run(s, "--yes");

        Assert.Equal(0, exit);
        Assert.Contains("No changes", output);
        Assert.Empty(s.Planned);
        Assert.Empty(s.Applied);
        Assert.False(File.Exists(Assert.Single(s.EditorPaths)));
    }

    [Fact]
    public async Task Failing_editor_still_deletes_the_temp_file()
    {
        var s = new Stubs { EditorFails = new InvalidOperationException("editor failed") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(s, "--yes"));

        Assert.False(File.Exists(Assert.Single(s.EditorPaths)));
        Assert.Empty(s.Planned);
    }

    [Fact]
    public async Task Json_prints_the_export_without_starting_the_editor()
    {
        var s = new Stubs();

        var (exit, output) = await Run(s, "--json");

        Assert.Equal(0, exit);
        Assert.Equal(Exported, output.Trim());
        Assert.Empty(s.EditorPaths);
        Assert.Empty(s.Planned);
    }
}
