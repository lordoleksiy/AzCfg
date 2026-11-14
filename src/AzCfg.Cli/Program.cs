using AzCfg.Cli;
using AzCfg.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

var app = new CommandApp(new TypeRegistrar(new ServiceCollection().AddAzCfg()));
app.Configure(config =>
{
    config.SetApplicationName("azcfg");
    config.SetExceptionHandler((ex, _) =>
    {
        var json = args.Contains("--json");
        (json ? Console.Out : Console.Error).WriteLine(CliErrors.Format(ex, json));
        return 1;
    });
});

return await app.RunAsync(args);
