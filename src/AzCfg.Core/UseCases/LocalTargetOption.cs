using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>
/// A local target offered to the developer during project resolution (decision 2). <see cref="Exists"/>
/// says whether the file is already present. <see cref="IsSafeForSecrets"/> is <c>true</c> for
/// <see cref="LocalTargetKind.UserSecrets"/> (it lives outside the repo); for a file inside the repo it is
/// <c>true</c> only when the file is git-ignored and not tracked.
/// </summary>
public sealed record LocalTargetOption(LocalTargetBinding Target, bool Exists, bool IsSafeForSecrets);
