using System.Xml;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Ui.Tests.Application.Workspace.Fakes;

/// <summary>A detector that fails like the real one on a malformed <c>.csproj</c>.</summary>
internal sealed class MalformedCsprojDetector : IProjectDetector
{
    public Task<IReadOnlyList<ProjectInfo>> DetectAsync(string repoPath, CancellationToken ct) =>
        Task.FromException<IReadOnlyList<ProjectInfo>>(new XmlException("Malformed project file 'Crm.csproj'."));
}
