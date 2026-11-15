using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>
/// Resolves and binds projects. Service candidates are the prefixes of the <c>dev</c> environment store (the first
/// environment when none is named <c>dev</c>), matched to csproj/folder names ignoring case and treating <c>-</c> and
/// <c>.</c> as equal; exactly one match is confident. <see cref="BindAsync"/> throws
/// <see cref="InvalidOperationException"/> when the secrets target is not safe for secrets.
/// </summary>
public sealed class ProjectService(
    IUserStateStore state,
    IProjectDetector detector,
    IConfigStore store,
    IEnvironmentResolver resolver,
    IEnumerable<ILocalTarget> targets) : IProjectService
{
    public async Task<ProjectResolution> ResolveAsync(string repoPath, CancellationToken ct)
    {
        if (await state.LoadBindingAsync(repoPath, ct) is { } existing)
        {
            return new([], true, [], existing);
        }

        var projects = await detector.DetectAsync(repoPath, ct);
        var profile = await state.LoadProfileAsync(ct);
        IReadOnlyList<ConfigEnvironment> environments = profile is null ? [] : resolver.Resolve(profile);
        var dev = environments.FirstOrDefault(e => e.Name.Equals("dev", StringComparison.OrdinalIgnoreCase))
            ?? environments.FirstOrDefault();
        var prefixes = dev is null ? [] : await store.ListServicePrefixesAsync(dev, ct).ToListAsync(ct);

        var names = projects
            .SelectMany(p => new[] { p.Name, Path.GetFileName(Path.GetDirectoryName(p.CsprojPath)) ?? p.Name })
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = prefixes.Where(p => names.Contains(Normalize(p))).ToList();

        var options = new List<LocalTargetOption>();
        foreach (var project in projects)
        {
            foreach (var target in targets.Where(t => t.CanHandle(project)))
            {
                var format = target.Kind == LocalTargetKind.LocalSettings ? LocalFileFormat.FlatDictionary : LocalFileFormat.NestedJson;
                options.Add(await target.InspectAsync(new(target.Kind, project.CsprojPath, format), ct));
            }
        }

        return new(matches.Count > 0 ? matches : prefixes, matches.Count == 1, options, null);
    }

    public async Task<ProjectBinding> BindAsync(
        string repoPath,
        string service,
        LocalTargetBinding valuesTarget,
        LocalTargetBinding secretsTarget,
        CancellationToken ct)
    {
        var option = await targets.First(t => t.Kind == secretsTarget.Kind).InspectAsync(secretsTarget, ct);
        if (!option.IsSafeForSecrets)
        {
            throw new InvalidOperationException($"{secretsTarget.Kind} target is not safe for secrets.");
        }

        var binding = new ProjectBinding(repoPath, service, valuesTarget, secretsTarget, new Dictionary<string, Uri>());
        await state.SaveBindingAsync(binding, ct);
        return binding;
    }

    private static string Normalize(string name) => name.Replace('-', '.');
}
