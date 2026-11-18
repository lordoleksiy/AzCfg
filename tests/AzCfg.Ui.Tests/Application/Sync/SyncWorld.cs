using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using AzCfg.Infrastructure;
using AzCfg.Infrastructure.InMemory;
using AzCfg.Ui.Application.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Application.Sync;

/// <summary>One project (service <c>crm</c>, user-secrets for values and secrets) over the in-memory Azure world.</summary>
internal sealed class SyncWorld
{
    public const string Sentinel = "AZCFG-TEST-SECRET-7f3a";
    public const string Repo = "C:/src/Crm";

    private readonly ServiceProvider _services;

    public SyncWorld(string env = "dev", bool isProtected = false, bool withVault = true)
    {
        Store = $"https://appcs-{env}.azconfig.io";
        Vault = $"https://kv-{env}.vault.azure.net";
        World = new InMemoryWorld();
        _services = new ServiceCollection().AddAzCfg().AddAzCfgInMemory(World).AddSyncServices().BuildServiceProvider();
        Env = new ConfigEnvironment(env, new Uri(Store), null, isProtected);
        var target = new LocalTargetBinding(LocalTargetKind.UserSecrets, $"{Repo}/Crm.csproj", LocalFileFormat.NestedJson);
        Binding = new ProjectBinding(Repo, "crm", target, target, withVault ? new Dictionary<string, Uri> { [env] = new(Vault) } : new Dictionary<string, Uri>());
        World.Discovery.AddVault($"kv-{env}", Vault);
    }

    public InMemoryWorld World { get; }

    public string Store { get; }

    public string Vault { get; }

    public ConfigEnvironment Env { get; }

    public ProjectBinding Binding { get; private set; }

    public ISyncService Sync => _services.GetRequiredService<ISyncService>();

    public ISyncWorkflowService Workflow => _services.GetRequiredService<ISyncWorkflowService>();

    public IDraftService Drafts => _services.GetRequiredService<IDraftService>();

    public CancellationToken Ct => CancellationToken.None;

    public void Seed(string key, string value) => World.AppConfig.Set(Store, $"crm:{key}", value);

    public void SeedGlobal(string key, string value) => World.AppConfig.Set(Store, $"global:{key}", value);

    public void SeedSecret(string key, string value) =>
        World.AppConfig.SetSecretReference(Store, $"crm:{key}", World.Secrets.Add(Vault, $"crm--{key.Replace(":", "--", StringComparison.Ordinal)}", value));

    public string? Remote(string key) => World.AppConfig.Get(Store, $"crm:{key}")?.Text;

    /// <summary>Forced pull of the environment; the binding afterwards has it as the active one.</summary>
    public async Task PullAsync()
    {
        await Sync.PullAsync(Binding, Env, null, true, Ct);
        Binding = Binding with { ActiveEnvironment = Env.Name };
    }

    public ConfigState? Snapshot() => World.State.Snapshot(Repo, Env.Name);

    public RemoteDraft Draft(string json) => new(Binding, Env, json, json, new Dictionary<DraftKey, DraftSecret>());
}
