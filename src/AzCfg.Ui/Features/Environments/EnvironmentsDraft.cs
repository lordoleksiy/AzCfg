using AzCfg.Core.Model;

namespace AzCfg.Ui.Features.Environments;

/// <summary>
/// The editable copy of a <see cref="UserProfile"/> behind the setup wizard and the Settings tab. <see cref="Validate"/> mirrors
/// the rules of <c>IEnvironmentService.SaveAsync</c> (name, unique name, https endpoint) so errors show inline before saving;
/// <see cref="ToProfile"/> may be called only for a draft without issues.
/// </summary>
public sealed class EnvironmentsDraft
{
    public EnvironmentMode Mode { get; set; }

    public string? DefaultLabel { get; set; }

    public bool GitIgnoreLocalFiles { get; set; }

    public List<EnvironmentRow> Rows { get; } = [];

    /// <summary>The saved environments as existing rows (names fixed); an empty draft for a first run (<paramref name="profile"/> null).</summary>
    public static EnvironmentsDraft From(UserProfile? profile)
    {
        var draft = new EnvironmentsDraft();
        if (profile is null)
        {
            return draft;
        }

        draft.Mode = profile.Mode;
        draft.DefaultLabel = profile.DefaultLabel;
        draft.GitIgnoreLocalFiles = profile.GitIgnoreLocalFiles;
        draft.Rows.AddRange(profile.Environments.Select(e => new EnvironmentRow
        {
            Name = e.Name,
            Endpoint = e.StoreEndpoint.ToString(),
            Label = e.Label,
            IsProtected = e.IsProtected,
        }));
        return draft;
    }

    /// <summary>The name a short environment gets for a long one (<c>Development</c> → <c>dev</c>); same rule as <c>azcfg init</c>.</summary>
    public static string SuggestName(string label) => label.Trim().ToLowerInvariant() switch
    {
        "development" => "dev",
        "staging" or "stg" => "stage",
        "production" or "prd" => "prod",
        var name => name,
    };

    /// <summary>Problems per included row, then of the whole draft; <paramref name="requireOne"/> also demands at least one environment.</summary>
    public IReadOnlyList<EnvironmentIssue> Validate(bool requireOne = false)
    {
        var issues = new List<EnvironmentIssue>();
        var included = Rows.Where(r => r.Included).ToList();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in included)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                issues.Add(new EnvironmentIssue(row, "Name is required"));
            }
            else if (!names.Add(row.Name.Trim()))
            {
                issues.Add(new EnvironmentIssue(row, "Duplicate name"));
            }

            if (!IsHttps(row.Endpoint))
            {
                issues.Add(new EnvironmentIssue(row, "Endpoint must be an absolute https URL"));
            }
        }

        if (Mode == EnvironmentMode.LabelPerEnvironment && included.Select(r => r.Endpoint.Trim().TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            issues.Add(new EnvironmentIssue(null, "Label mode uses one shared store: every environment needs the same endpoint"));
        }

        if (requireOne && included.Count == 0)
        {
            issues.Add(new EnvironmentIssue(null, "Add at least one environment"));
        }

        return issues;
    }

    /// <summary>The profile to save: included rows trimmed, blank labels and a blank default label as null.</summary>
    public UserProfile ToProfile() => new(
        [.. Rows.Where(r => r.Included).Select(r => new ConfigEnvironment(r.Name.Trim(), new Uri(r.Endpoint.Trim()), Blank(r.Label), r.IsProtected))],
        Mode,
        Blank(DefaultLabel),
        GitIgnoreLocalFiles);

    /// <summary>Names of <paramref name="original"/>'s environments that this draft no longer includes (G30).</summary>
    public IReadOnlyList<string> RemovedFrom(UserProfile? original) =>
        original is null
            ? []
            : [.. original.Environments.Select(e => e.Name).Where(n => !Rows.Any(r => r.Included && string.Equals(r.Name.Trim(), n, StringComparison.OrdinalIgnoreCase)))];

    private static bool IsHttps(string endpoint) =>
        Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
