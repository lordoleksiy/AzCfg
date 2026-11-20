using AzCfg.Core.Model;
using AzCfg.Ui.Application.Configuration;

namespace AzCfg.Ui.Tests.Features.Browse.Fakes;

/// <summary>Records the local writes; secret values are never logged.</summary>
internal sealed class RecordingLocalValues : ILocalValuesService
{
    public List<string> Calls { get; } = [];

    public Task SetPlainAsync(ProjectBinding binding, string key, string value, CancellationToken ct) => Log($"SetPlain {key}={value}");

    public Task SetSecretAsync(ProjectBinding binding, string key, SecretString value, CancellationToken ct) => Log($"SetSecret {key}");

    public Task RemoveAsync(ProjectBinding binding, string key, CancellationToken ct) => Log($"Remove {key}");

    public Task SetScopeAsync(ProjectBinding binding, string key, ConfigScope scope, CancellationToken ct) => Log($"SetScope {key} {scope}");

    private Task Log(string call)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }
}
