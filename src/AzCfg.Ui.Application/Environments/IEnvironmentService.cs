using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Environments;

public interface IEnvironmentService
{
    Task<EnvironmentsSnapshot> LoadAsync(CancellationToken ct);

    Task<IReadOnlyList<EnvironmentCandidate>> DiscoverAsync(CancellationToken ct);

    Task<IReadOnlyList<string>> ListLabelsAsync(Uri store, CancellationToken ct);

    /// <summary>
    /// Saves the whole profile as given (Mode, DefaultLabel, GitIgnoreLocalFiles included) and returns the new snapshot.
    /// <see cref="ArgumentException"/>: empty or duplicate name (OrdinalIgnoreCase), endpoint not an absolute https URI.
    /// </summary>
    Task<EnvironmentsSnapshot> SaveAsync(UserProfile profile, CancellationToken ct);
}
