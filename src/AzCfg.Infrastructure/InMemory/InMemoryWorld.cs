namespace AzCfg.Infrastructure.InMemory;

/// <summary>
/// The in-memory Azure + user state of one test or demo session: App Configuration, Key Vault, user state, the user-secrets
/// file, resource discovery and the credential. Wire it with <see cref="InMemoryServiceCollectionExtensions.AddAzCfgInMemory"/>.
/// </summary>
public sealed class InMemoryWorld
{
    public InMemoryAppConfig AppConfig { get; } = new();

    public InMemorySecretStore Secrets { get; } = new();

    public InMemoryUserStateStore State { get; } = new();

    public InMemoryProjectManifestStore Manifests { get; } = new();

    public InMemoryLocalTarget UserSecrets { get; } = new();

    public FixedResourceDiscovery Discovery { get; } = new();

    public StaticTokenCredential Credential { get; init; } = new();

    /// <summary><c>null</c> keeps the real <c>CsprojDetector</c> (scans real folders).</summary>
    public FixedProjectDetector? Detector { get; init; }
}
