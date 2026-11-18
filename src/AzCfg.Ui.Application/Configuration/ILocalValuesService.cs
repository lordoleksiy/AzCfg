using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>
/// Writes through <c>ILocalTarget.ReadAsync/WriteAsync</c> (full rewrite, ADR 0009 formatting) and keeps the key's scope
/// marker. Plain → ValuesTarget (removed from SecretsTarget); secret → SecretsTarget (removed from ValuesTarget). Writing a
/// secret re-checks <c>InspectAsync(SecretsTarget).IsSafeForSecrets</c> → <see cref="InvalidOperationException"/>, nothing written.
/// </summary>
public interface ILocalValuesService
{
    Task SetPlainAsync(ProjectBinding binding, string key, string value, CancellationToken ct);

    Task SetSecretAsync(ProjectBinding binding, string key, SecretString value, CancellationToken ct);

    /// <summary>No-op when the key is missing.</summary>
    Task RemoveAsync(ProjectBinding binding, string key, CancellationToken ct);

    /// <summary>Sets or clears the <c>//@global</c> marker.</summary>
    Task SetScopeAsync(ProjectBinding binding, string key, ConfigScope scope, CancellationToken ct);
}
