using AzCfg.Infrastructure.State;
using Microsoft.Extensions.Configuration;

namespace AzCfg.Ui.Hosting;

/// <summary>
/// Command-line options of the UI host, bound from the configuration keys <c>port</c>, <c>no-browser</c>, <c>demo</c>
/// and <c>state-root</c>. <see cref="Demo"/>: null (real Azure), <c>"full"</c> or <c>"empty"</c>.
/// </summary>
public sealed class UiHostOptions
{
    public int Port { get; set; }

    [ConfigurationKeyName("no-browser")]
    public bool NoBrowser { get; set; }

    /// <summary>Print the login URL as one JSON line <c>{ "url": … }</c> instead of a bare URL (<c>azcfg ui --json</c>).</summary>
    public bool Json { get; set; }

    public string? Demo { get; set; }

    [ConfigurationKeyName("state-root")]
    public string StateRoot { get; set; } = JsonUserStateStore.DefaultRoot;
}
