using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Features.Browse;

/// <summary>Small pure helpers over <see cref="ConfigRow"/> shared by the Browse view, menu and inspector.</summary>
public static class BrowseRows
{
    /// <summary>The remote scope, else the local one, else service.</summary>
    public static ConfigScope ScopeOf(ConfigRow row) => row.Remote?.Scope ?? row.Local?.Scope ?? ConfigScope.Service;

    public static DraftKey DraftKeyOf(ConfigRow row) => new(ScopeOf(row), row.Key);

    /// <summary>The Azure value of a plain key; null for a secret or a missing key.</summary>
    public static string? PlainRemote(ConfigRow row) => row.Remote is { IsSecret: false, Value: { } value } ? value : null;

    /// <summary>The local value of a plain, unmasked key; null otherwise.</summary>
    public static string? PlainLocal(ConfigRow row) => row.Local is { Masked: false, Value: { } value } ? value : null;

    /// <summary>"user-secrets", "appsettings.local.json" or "local.settings.json".</summary>
    public static string SourceName(LocalTargetBinding target) => target.Kind switch
    {
        LocalTargetKind.UserSecrets => "user-secrets",
        LocalTargetKind.AppSettingsLocal => "appsettings.local.json",
        _ => "local.settings.json",
    };

    /// <summary>"vault-host/secret-name" of a Key Vault reference.</summary>
    public static string ReferenceText(RemoteCell cell) =>
        cell.VaultHost is { } host && cell.SecretName is { } name ? $"{host}/{name}" : cell.SecretName ?? "Key Vault secret";

    public static string StatusText(DiffKind? status, bool ignored) =>
        ignored ? "ignored"
        : status switch
        {
            DiffKind.LocalAdded => "Added locally",
            DiffKind.LocalModified => "Modified locally",
            DiffKind.LocalDeleted => "Deleted locally",
            DiffKind.RemoteAdded => "Added in Azure",
            DiffKind.RemoteModified => "Changed in Azure",
            DiffKind.RemoteDeleted => "Deleted in Azure",
            DiffKind.Conflict => "Conflict",
            DiffKind.Unchanged => "In sync",
            _ => "Remote only",
        };
}
