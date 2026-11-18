using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Ui.Application.Environments;

/// <summary>
/// <see cref="IEnvironmentService"/> over the saved <see cref="UserProfile"/>. The profile is saved exactly as given — resolved
/// environments are never written back (G26).
/// </summary>
public sealed class EnvironmentService(IUserStateStore state, IEnvironmentResolver resolver, ISetupService setup) : IEnvironmentService
{
    public async Task<EnvironmentsSnapshot> LoadAsync(CancellationToken ct)
    {
        var profile = await state.LoadProfileAsync(ct);
        return new EnvironmentsSnapshot(profile, profile is null ? [] : resolver.Resolve(profile));
    }

    public Task<IReadOnlyList<EnvironmentCandidate>> DiscoverAsync(CancellationToken ct) => setup.DiscoverEnvironmentsAsync(ct);

    public Task<IReadOnlyList<string>> ListLabelsAsync(Uri store, CancellationToken ct) => setup.ListLabelsAsync(store, ct);

    public async Task<EnvironmentsSnapshot> SaveAsync(UserProfile profile, CancellationToken ct)
    {
        Validate(profile);
        await setup.SaveProfileAsync(profile, ct);
        return await LoadAsync(ct);
    }

    private static void Validate(UserProfile profile)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var env in profile.Environments)
        {
            if (string.IsNullOrWhiteSpace(env.Name))
            {
                throw new ArgumentException("Environment name is required.", nameof(profile));
            }

            if (!names.Add(env.Name.Trim()))
            {
                throw new ArgumentException($"Duplicate environment name '{env.Name}'.", nameof(profile));
            }

            if (env.StoreEndpoint is not { IsAbsoluteUri: true, Scheme: "https" })
            {
                throw new ArgumentException($"The endpoint of '{env.Name}' must be an absolute https URL.", nameof(profile));
            }
        }
    }
}
