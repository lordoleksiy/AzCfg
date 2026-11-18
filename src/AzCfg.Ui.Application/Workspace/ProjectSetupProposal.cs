using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Ui.Application.Workspace;

public sealed record ProjectSetupProposal(ProjectInfo Project, ProjectResolution Resolution);
