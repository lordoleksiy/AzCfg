using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Cli.Commands;

/// <summary>The <c>status</c> result: the project's binding facts plus the <see cref="SyncSummary"/> as-is (keys and counters, never values).</summary>
public sealed record StatusReport(
    string Project,
    string Service,
    LocalTargetBinding ValuesTarget,
    LocalTargetBinding SecretsTarget,
    string? ActiveEnvironment,
    SyncSummary Summary);
