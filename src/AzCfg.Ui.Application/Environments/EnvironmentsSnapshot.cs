using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Environments;

/// <summary>Profile as saved plus the environments resolved for operations; <see cref="Environments"/> is empty when <see cref="Profile"/> is null.</summary>
public sealed record EnvironmentsSnapshot(UserProfile? Profile, IReadOnlyList<ConfigEnvironment> Environments);
