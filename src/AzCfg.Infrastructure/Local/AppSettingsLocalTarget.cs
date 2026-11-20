using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// <see cref="LocalTargetKind.AppSettingsLocal"/>: <c>appsettings.local.json</c> next to the project's
/// <c>.csproj</c> (<see cref="LocalTargetBinding.ProjectPath"/>), nested JSON (ADR 0006). Offered only for
/// application projects (not libraries) that already have any <c>appsettings*.json</c> (e.g. <c>appsettings.json</c>,
/// <c>appsettings.Development.json</c>, <c>appsettings.Local.json</c>; case-insensitive) next to the <c>.csproj</c>
/// (see <see cref="CanHandle"/>). An existing local file is matched case-insensitively and keeps its real name
/// (e.g. <c>appsettings.Local.json</c>); otherwise <c>appsettings.local.json</c> is used. A secrets target must live outside git's
/// reach: <see cref="InspectAsync"/> reports it safe only when the file is git-ignored
/// (<c>git check-ignore -q</c>) and not already tracked (<c>git ls-files --error-unmatch</c> fails);
/// outside a git repository it is always unsafe.
/// </summary>
public sealed class AppSettingsLocalTarget : JsonFileTarget
{
    private const string DefaultFileName = "appsettings.local.json";

    public override LocalTargetKind Kind => LocalTargetKind.AppSettingsLocal;

    public override bool CanHandle(ProjectInfo project) =>
        !project.IsLibrary
        && Path.GetDirectoryName(project.CsprojPath) is { } directory
        && Directory.Exists(directory)
        && Directory.EnumerateFiles(directory, "*.json")
            .Any(f => Path.GetFileName(f).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase));

    public override async Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = GetFilePath(target);
        var exists = File.Exists(path);
        var isSafeForSecrets = await GitIgnoreGuard.IsSafeForSecretsAsync(path, ct);

        return new LocalTargetOption(target, exists, isSafeForSecrets);
    }

    public override Task EnsureGitIgnoredAsync(LocalTargetBinding target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        return GitIgnoreGuard.EnsureIgnoredAsync(GetFilePath(target), ct);
    }

    protected override string GetFilePath(LocalTargetBinding target)
    {
        var directory = Path.GetDirectoryName(target.ProjectPath);
        var fileName = FindLocalFileName(string.IsNullOrEmpty(directory) ? "." : directory) ?? DefaultFileName;
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    private static string? FindLocalFileName(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json")
                .Select(Path.GetFileName)
                .FirstOrDefault(f => string.Equals(f, DefaultFileName, StringComparison.OrdinalIgnoreCase))
            : null;
}
