using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

public interface IConfigurationService
{
    /// <summary>
    /// Active env: <c>DiffAsync</c> + <c>ReadRemoteConfigAsync</c> + <c>ReadAsync</c> of each target (Source, Scope, ignored keys).
    /// Other env: <c>ReadRemoteConfigAsync</c> only — <see cref="ConfigurationView.IsRemoteOnly"/>, no <c>ISecretStore</c> call.
    /// Masking per <c>SecretPolicy</c>. Ignored keys are rows with <c>IsIgnored = true</c> and <c>Status = null</c>. Rows ordered
    /// by key (ordinal).
    /// </summary>
    Task<ConfigurationView> LoadAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct);

    /// <summary>
    /// Remote: <c>ToEditJson</c> of the remote entries (<c>{"@kv": name}</c>, <c>"@global"</c>). Local: nested JSON of local
    /// values with secret values replaced by <c>"***"</c>.
    /// </summary>
    string ToJson(ConfigurationView view, ConfigSide side);
}
