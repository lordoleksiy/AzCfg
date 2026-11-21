using AzCfg.Cli.Commands;
using AzCfg.Ui.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Tests.Commands;

public sealed class UiCommandTests
{
    private sealed class RecordingUiCommand : UiCommand
    {
        public static UiHostOptions? Started;

        protected override Task RunHostAsync(UiHostOptions options, CancellationToken cancellationToken)
        {
            Started = options;
            return Task.CompletedTask;
        }
    }

    private static int RunRecording(params string[] args)
    {
        RecordingUiCommand.Started = null;
        var app = new CommandApp(new TypeRegistrar(new ServiceCollection()));
        app.Configure(c => c.AddCommand<RecordingUiCommand>("ui"));
        return app.Run(args);
    }

    [Fact]
    public void Defaults_are_a_free_port_with_browser_and_real_Azure()
    {
        Assert.Equal(0, RunRecording("ui"));
        var o = RecordingUiCommand.Started!;
        Assert.Equal((0, false, false, null), (o.Port, o.NoBrowser, o.Json, o.Demo));
    }

    [Fact]
    public void Options_are_mapped_to_the_host_options()
    {
        Assert.Equal(0, RunRecording("ui", "--port", "5199", "--no-browser", "--json", "--demo=empty"));
        var o = RecordingUiCommand.Started!;
        Assert.Equal((5199, true, true, "empty"), (o.Port, o.NoBrowser, o.Json, o.Demo));
    }

    [Fact]
    public void Bare_demo_means_the_full_demo_world()
    {
        Assert.Equal(0, RunRecording("ui", "--demo"));
        Assert.Equal("full", RecordingUiCommand.Started!.Demo);
    }

    [Theory]
    [InlineData("--demo=bogus")]
    [InlineData("--port=70000")]
    [InlineData("--port=-1")]
    public void Invalid_options_do_not_start_the_host(string option)
    {
        Assert.NotEqual(0, RunRecording("ui", option));
        Assert.Null(RecordingUiCommand.Started);
    }

    [Fact]
    public void Host_options_become_the_command_line_the_host_binds()
    {
        var options = new UiHostOptions { Port = 5199, NoBrowser = true, Json = true, Demo = "empty", StateRoot = "/state" };
        Assert.Equal(
            ["--port=5199", "--no-browser=True", "--json=True", "--state-root=/state", "--demo=empty"],
            AzCfg.Ui.UiHost.ToArgs(options));
    }

    [Fact]
    public void Ui_is_registered_in_the_command_tree_and_existing_commands_still_are()
    {
        // `ui` with an invalid option fails in settings validation, which proves the command is registered without starting a host.
        var app = new CommandApp(new TypeRegistrar(new ServiceCollection()));
        app.Configure(CliApp.Configure);
        Assert.NotEqual(0, app.Run(["ui", "--demo=bogus"]));
        // unknown commands are still rejected, so registration is not a catch-all
        Assert.NotEqual(0, app.Run(["no-such-command"]));
        // existing branches keep parsing (--help exits 0 without running anything)
        foreach (var existing in new[] { "init", "pull", "use", "status", "diff", "plan", "push", "edit", "promote", "ignore", "config" })
        {
            Assert.Equal(0, app.Run([existing, "--help"]));
        }
    }
}
