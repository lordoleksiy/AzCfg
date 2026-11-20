using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Ui.Application.Configuration;

namespace AzCfg.Ui.Tests.Features.Browse.Fakes;

/// <summary>Answers with <see cref="Rows"/> (remote-only for a non-active environment), records loads, can be held back with <see cref="Gate"/> or fail.</summary>
internal sealed class FakeConfigurationService : IConfigurationService
{
    public List<string> Loads { get; } = [];

    public IReadOnlyList<ConfigRow> Rows { get; set; } = [];

    public IReadOnlyList<Core.Sync.SyncWarning> Warnings { get; set; } = [];

    public string RemoteJson { get; set; } = "{}";

    public string LocalJson { get; set; } = "{}";

    public TaskCompletionSource? Gate { get; set; }

    public Exception? Failure { get; set; }

    public async Task<ConfigurationView> LoadAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
    {
        Loads.Add(env.Name);
        if (Gate is { } gate)
        {
            await gate.Task;
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        return new ConfigurationView(binding, env, !EnvironmentRules.IsActive(binding, env), Rows, Warnings, DateTimeOffset.UnixEpoch.AddSeconds(Loads.Count));
    }

    public string ToJson(ConfigurationView view, ConfigSide side) => side == ConfigSide.Remote ? RemoteJson : LocalJson;
}
