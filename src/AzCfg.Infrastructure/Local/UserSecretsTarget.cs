using System.Diagnostics;
using System.Xml.Linq;
using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// <see cref="LocalTargetKind.UserSecrets"/>: the project's dotnet user-secrets file, nested JSON (ADR 0006).
/// Always safe for secrets, since it lives outside the repo. A project without <c>UserSecretsId</c> reads as
/// empty and is not modified by reading or <see cref="InspectAsync"/>; <see cref="JsonFileTarget.WriteAsync"/> runs
/// <c>dotnet user-secrets init</c> first and throws <see cref="InvalidOperationException"/> if that fails.
/// </summary>
public sealed class UserSecretsTarget : JsonFileTarget
{
    public override LocalTargetKind Kind => LocalTargetKind.UserSecrets;

    public override bool CanHandle(ProjectInfo project) => true;

    public override Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct) =>
        Task.FromResult(new LocalTargetOption(target, GetReadFilePath(target) is { } path && File.Exists(path), true));

    protected override string? GetReadFilePath(LocalTargetBinding target) =>
        ReadSecretsId(target.ProjectPath) is { } id ? SecretsPath(id) : null;

    protected override string GetFilePath(LocalTargetBinding target) =>
        SecretsPath(ReadSecretsId(target.ProjectPath) ?? InitSecretsId(target.ProjectPath));

    private static string SecretsPath(string id) => PathHelper.GetSecretsPathFromSecretsId(id);

    private static string? ReadSecretsId(string csprojPath) =>
        XDocument.Load(csprojPath).Descendants().FirstOrDefault(e => e.Name.LocalName == "UserSecretsId")?.Value.Trim();

    private static string InitSecretsId(string csprojPath)
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "user-secrets", "init", "--project", csprojPath },
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0 && ReadSecretsId(csprojPath) is { } id
            ? id
            : throw new InvalidOperationException($"'dotnet user-secrets init' failed for {csprojPath}: {error}");
    }
}
