using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;

namespace AzCfg.Ui.Tests.Features.Environments.Fakes;

internal sealed class FakeEnvironmentService : IEnvironmentService
{
    public EnvironmentsSnapshot Snapshot { get; set; } = new(null, []);

    public IReadOnlyList<EnvironmentCandidate> Candidates { get; set; } = [];

    public IReadOnlyList<string> Labels { get; set; } = [];

    public Exception? DiscoverFailure { get; set; }

    public Exception? SaveFailure { get; set; }

    public Exception? LoadFailure { get; set; }

    public int Loads { get; private set; }

    public List<UserProfile> Saved { get; } = [];

    public Task<EnvironmentsSnapshot> LoadAsync(CancellationToken ct)
    {
        Loads++;
        return LoadFailure is null ? Task.FromResult(Snapshot) : throw LoadFailure;
    }

    public Task<IReadOnlyList<EnvironmentCandidate>> DiscoverAsync(CancellationToken ct) =>
        DiscoverFailure is null ? Task.FromResult(Candidates) : throw DiscoverFailure;

    public Task<IReadOnlyList<string>> ListLabelsAsync(Uri store, CancellationToken ct) => Task.FromResult(Labels);

    public Task<EnvironmentsSnapshot> SaveAsync(UserProfile profile, CancellationToken ct)
    {
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        Saved.Add(profile);
        Snapshot = new EnvironmentsSnapshot(profile, new ProfileEnvironmentResolver().Resolve(profile));
        return Task.FromResult(Snapshot);
    }
}
