using AzCfg.Cli;
using AzCfg.Cli.Commands;
using AzCfg.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp(new TypeRegistrar(new ServiceCollection().AddAzCfg().AddSingleton(AnsiConsole.Console).AddSingleton<IEditorLauncher, ProcessEditorLauncher>()));
app.Configure(config =>
{
    config.SetApplicationName("azcfg");
    config.AddCommand<InitCommand>("init").WithDescription("Discover App Configuration stores and save your environments.");
    config.AddCommand<PullCommand>("pull").WithDescription("Pull an environment into the local targets.");
    config.AddCommand<UseCommand>("use").WithDescription("Pull an environment and make it the active one.");
    config.AddCommand<PlanCommand>("plan").WithDescription("Show what pushing the local files would change.");
    config.AddCommand<PushCommand>("push").WithDescription("Apply the local files to an environment.");
    config.AddCommand<EditCommand>("edit").WithDescription("Edit the remote config as JSON in your editor, then apply it.");
    config.SetExceptionHandler((ex, _) =>
    {
        var json = args.Contains("--json");
        (json ? Console.Out : Console.Error).WriteLine(CliErrors.Format(ex, json));
        return 1;
    });
});

return await app.RunAsync(args);
