using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Tests.Features.Workspace.Fakes;

internal sealed class FakeBindingFlow : IBindingFlow
{
    public List<string> Calls { get; } = [];

    public bool Result { get; set; } = true;

    public Task<bool> BindAsync(string projectId)
    {
        Calls.Add(projectId);
        return Task.FromResult(Result);
    }
}
