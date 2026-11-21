using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using AzCfg.Infrastructure;
using AzCfg.Infrastructure.InMemory;
using AzCfg.Ui.Application.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzCfg.Ui.Tests.Application.Configuration;

/// <summary>
/// Real Core services over the in-memory world; values go to a real <c>appsettings.local.json</c> in a temp folder, secrets to the
/// in-memory user-secrets file. Service <c>crm</c>, environments <c>dev</c> (active) and <c>stg</c>.
/// </summary>
internal sealed class ConfigurationWorld : IDisposable
{
    public const string Sentinel = "AZCFG-TEST-SECRET-7f3a";
    public const string Vault = "https://kv.vault.azure.net";

    public static readonly ConfigEnvironment Dev = new("dev", new Uri("https://dev.azconfig.io"), null, false);
    public static readonly ConfigEnvironment Stg = new("stg", new Uri("https://stg.azconfig.io"), null, false);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "azcfg-ui-tests", Guid.NewGuid().ToString("N"));

    public ConfigurationWorld(string? active = "dev", IReadOnlyList<string>? ignore = null, LocalTargetKind valuesKind = LocalTargetKind.AppSettingsLocal)
    {
        Directory.CreateDirectory(_folder);
        CsprojPath = Path.Combine(_folder, "Crm.csproj");
        File.WriteAllText(CsprojPath, "<Project />");
        Values = new(valuesKind, CsprojPath, valuesKind == LocalTargetKind.LocalSettings ? LocalFileFormat.FlatDictionary : LocalFileFormat.NestedJson);
        Secrets = new(LocalTargetKind.UserSecrets, CsprojPath, LocalFileFormat.NestedJson);
        Binding = new(_folder, "crm", Values, Secrets, new Dictionary<string, Uri>(), active, ignore);
        SecretStore = new(World.Secrets);
        Provider = new ServiceCollection()
            .AddAzCfg()
            .AddAzCfgInMemory(World)
            .Replace(ServiceDescriptor.Singleton<ISecretStore>(SecretStore))
            .AddConfigurationServices()
            .BuildServiceProvider();
    }

    public InMemoryWorld World { get; } = new();

    public CountingSecretStore SecretStore { get; }

    public ServiceProvider Provider { get; }

    public string CsprojPath { get; }

    public LocalTargetBinding Values { get; }

    public LocalTargetBinding Secrets { get; }

    public ProjectBinding Binding { get; }

    public string ValuesFile => Path.Combine(_folder, Values.Kind == LocalTargetKind.LocalSettings ? "local.settings.json" : "appsettings.local.json");

    public ISyncService Sync => Provider.GetRequiredService<ISyncService>();

    public IConfigurationService Configuration => Provider.GetRequiredService<IConfigurationService>();

    public ICompareService Compare => Provider.GetRequiredService<ICompareService>();

    public ILocalValuesService LocalValues => Provider.GetRequiredService<ILocalValuesService>();

    public ISecretRevealService Reveal => Provider.GetRequiredService<ISecretRevealService>();

    /// <summary>Seeds the remote secret <c>crm--Api--Key</c> with the sentinel and a reference at <c>crm:Api:Key</c> in <paramref name="env"/>.</summary>
    public Uri SeedRemoteSecret(ConfigEnvironment env, string value = Sentinel)
    {
        var uri = World.Secrets.Add(Vault, "crm--Api--Key", value);
        World.AppConfig.SetSecretReference(env.StoreEndpoint.ToString(), "crm:Api:Key", uri);
        return uri;
    }

    public void SeedRemote(ConfigEnvironment env, string key, string value, string? label = null) =>
        World.AppConfig.Set(env.StoreEndpoint.ToString(), key, value, label);

    public Task WriteValuesAsync(params LocalValue[] values) => Provider.GetServices<ILocalTarget>().First(t => t.Kind == Values.Kind).WriteAsync(Values, values, default);

    public Task WriteSecretsAsync(params LocalValue[] values) => World.UserSecrets.WriteAsync(Secrets, values, default);

    public Task PullAsync(ConfigEnvironment env) => Sync.PullAsync(Binding, env, null, true, default);

    public void Dispose()
    {
        Provider.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }
}
