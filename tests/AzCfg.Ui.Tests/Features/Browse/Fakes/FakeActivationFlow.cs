using AzCfg.Core.Model;
using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Tests.Features.Browse.Fakes;

internal sealed class FakeActivationFlow : IActivationFlow
{
    public List<(string ProjectId, string Environment)> Calls { get; } = [];

    public Task<bool> MakeActiveAsync(string projectId, ConfigEnvironment environment)
    {
        Calls.Add((projectId, environment.Name));
        return Task.FromResult(true);
    }
}
