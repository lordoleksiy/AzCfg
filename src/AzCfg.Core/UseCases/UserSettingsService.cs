using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>Reads and validates per-user global settings stored in the profile via <see cref="IUserStateStore"/> (ADR 0008).</summary>
public sealed class UserSettingsService(IUserStateStore state) : IUserSettingsService
{
    private const string ModeKey = "mode";
    private const string DefaultLabelKey = "defaultLabel";
    private const string EnvPrefix = "env.";
    private const string Endpoint = "endpoint";
    private const string Label = "label";
    private const string Protected = "protected";

    public async Task<IReadOnlyList<UserSetting>> ListAsync(CancellationToken ct) => List(await LoadAsync(ct));

    public async Task<UserSetting> GetAsync(string key, CancellationToken ct) => Find(await LoadAsync(ct), key);

    public async Task<UserSetting> SetAsync(string key, string? value, CancellationToken ct)
    {
        var profile = Apply(await LoadAsync(ct), key, value);
        await state.SaveProfileAsync(profile, ct);
        return Find(profile, key);
    }

    private async Task<UserProfile> LoadAsync(CancellationToken ct) =>
        await state.LoadProfileAsync(ct) ?? throw new InvalidOperationException("No profile found. Run 'azcfg init' first.");

    private static List<UserSetting> List(UserProfile profile) =>
    [
        new(ModeKey, EnvironmentModeNames.Format(profile.Mode)),
        new(DefaultLabelKey, profile.DefaultLabel),
        .. profile.Environments.SelectMany(e => new UserSetting[]
        {
            new(EnvKey(e.Name, Endpoint), e.StoreEndpoint.OriginalString),
            new(EnvKey(e.Name, Label), e.Label),
            new(EnvKey(e.Name, Protected), e.IsProtected ? "true" : "false"),
        }),
    ];

    private static UserSetting Find(UserProfile profile, string key)
    {
        var settings = List(profile);
        return settings.Find(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? throw UnknownKey(key, settings);
    }

    private static UserProfile Apply(UserProfile profile, string key, string? value)
    {
        if (key.Equals(ModeKey, StringComparison.OrdinalIgnoreCase))
        {
            return EnvironmentModeNames.TryParse(value, out var mode)
                ? profile with { Mode = mode }
                : throw Invalid(ModeKey, value, $"'{EnvironmentModeNames.Stores}' or '{EnvironmentModeNames.Labels}'");
        }

        if (key.Equals(DefaultLabelKey, StringComparison.OrdinalIgnoreCase))
            return profile with { DefaultLabel = LabelOrNull(value) };

        if (!TrySplitEnvKey(key, out var name, out var property))
            throw UnknownKey(key, List(profile));

        var index = FindEnvironment(profile, name);
        var env = profile.Environments[index];
        var canonical = EnvKey(env.Name, property);
        env = property switch
        {
            Endpoint => env with { StoreEndpoint = ParseEndpoint(canonical, value) },
            Label => env with { Label = LabelOrNull(value) },
            _ => env with { IsProtected = bool.TryParse(value?.Trim(), out var isProtected) ? isProtected : throw Invalid(canonical, value, "'true' or 'false'") },
        };
        return profile with { Environments = [.. profile.Environments.Select((e, i) => i == index ? env : e)] };
    }

    private static bool TrySplitEnvKey(string key, out string name, out string property)
    {
        name = property = "";
        var dot = key.LastIndexOf('.');
        if (!key.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase) || dot <= EnvPrefix.Length)
            return false;

        name = key[EnvPrefix.Length..dot];
        property = key[(dot + 1)..].ToLowerInvariant();
        return property is Endpoint or Label or Protected;
    }

    private static int FindEnvironment(UserProfile profile, string name)
    {
        var index = profile.Environments.ToList().FindIndex(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0
            ? index
            : throw new ArgumentException($"Unknown environment '{name}'. Known environments: {string.Join(", ", profile.Environments.Select(e => e.Name))}.");
    }

    private static Uri ParseEndpoint(string key, string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : throw Invalid(key, value, "an absolute http(s) URL, e.g. https://<name>.azconfig.io");

    private static string? LabelOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string EnvKey(string name, string property) => $"{EnvPrefix}{name}.{property}";

    private static ArgumentException Invalid(string key, string? value, string expected) =>
        new($"Invalid value '{value}' for '{key}': expected {expected}.");

    private static ArgumentException UnknownKey(string key, IEnumerable<UserSetting> settings) =>
        new($"Unknown setting '{key}'. Known settings: {string.Join(", ", settings.Select(s => s.Key))}.");
}
