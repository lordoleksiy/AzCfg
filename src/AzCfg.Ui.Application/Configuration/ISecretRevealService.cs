using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

public interface ISecretRevealService
{
    Task<SecretString> RevealRemoteAsync(Uri secretUri, CancellationToken ct);

    Task<SecretString?> RevealLocalAsync(ProjectBinding binding, string key, CancellationToken ct);
}
