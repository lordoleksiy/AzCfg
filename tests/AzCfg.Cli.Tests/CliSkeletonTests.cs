using System.Net;
using Azure;
using Azure.Identity;
using AzCfg.Cli;
using AzCfg.Cli.Commands;
using AzCfg.Core.Model;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace AzCfg.Cli.Tests;

public sealed class CliSkeletonTests
{
    private sealed class Marker;

    private sealed class ProbeCommand(Marker marker) : Command<GlobalSettings>
    {
        public static GlobalSettings? Seen;

        public override int Execute(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
        {
            Seen = settings;
            return marker is null ? 1 : 0;
        }
    }

    private sealed record Sample(string UserName, SecretString Token);

    private static int Run(params string[] args)
    {
        var app = new CommandApp(new TypeRegistrar(new ServiceCollection().AddSingleton<Marker>()));
        app.Configure(c => c.AddCommand<ProbeCommand>("probe"));
        return app.Run(args);
    }

    private static (string?, bool, bool, string) Parsed() =>
        (ProbeCommand.Seen!.Env, ProbeCommand.Seen.Json, ProbeCommand.Seen.Yes, ProbeCommand.Seen.Project);

    [Fact]
    public void Registrar_resolves_command_dependencies_from_DI() => Assert.Equal(0, Run("probe"));

    [Fact]
    public void Global_options_are_parsed()
    {
        Run("probe", "--env", "dev", "--json", "--yes", "--project", "p");
        Assert.Equal(("dev", true, true, "p"), Parsed());
    }

    [Fact]
    public void Global_options_have_defaults()
    {
        Run("probe");
        Assert.Equal((null, false, false, Directory.GetCurrentDirectory()), Parsed());
    }

    public static TheoryData<Exception, string> Errors => new()
    {
        { new AuthenticationFailedException("x"), "az login" },
        { new CredentialUnavailableException("x"), "az login" },
        { new RequestFailedException((int)HttpStatusCode.Unauthorized, "x"), "az login" },
        { new RequestFailedException((int)HttpStatusCode.Forbidden, "x"), "App Configuration Data Owner" },
        { new RequestFailedException((int)HttpStatusCode.Forbidden, "x"), "Key Vault Secrets User" },
        { new RequestFailedException((int)HttpStatusCode.NotFound, "missing"), "missing" },
        { new InvalidOperationException("plain"), "plain" },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Describe_maps_errors_to_actionable_messages(Exception ex, string expected) =>
        Assert.Contains(expected, CliErrors.Describe(ex));

    [Theory]
    [InlineData(true, """{"error":"plain"}""")]
    [InlineData(false, "Error: plain")]
    public void Format_follows_output_mode(bool json, string expected) =>
        Assert.Equal(expected, CliErrors.Format(new InvalidOperationException("plain"), json));

    [Fact]
    public void Json_is_camelCase_and_redacts_secrets() =>
        Assert.Equal("""{"userName":"a","token":"***"}""", CliJson.Serialize(new Sample("a", new SecretString("s3cret"))));
}
