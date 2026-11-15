namespace AzCfg.Core.Model;

/// <summary>Kinds of local file a developer can bind a project's values/secrets to (decision 1).</summary>
public enum LocalTargetKind
{
    /// <summary>dotnet user-secrets (secrets.json), nested JSON. MVP.</summary>
    UserSecrets,

    /// <summary>appsettings.local.json in the project directory, nested JSON. MVP.</summary>
    AppSettingsLocal,

    /// <summary>
    /// local.settings.json (Azure Functions). Only a flat string dictionary under <c>Values</c> is supported;
    /// nested JSON is forbidden for this kind. Designed in contracts, not implemented in MVP.
    /// </summary>
    LocalSettings,
}
