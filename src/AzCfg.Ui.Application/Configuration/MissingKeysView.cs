using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>Keys × environments (FR-C6). <see cref="Rows"/> hold every key; <see cref="MatrixRow.HasGap"/> comes from Core <c>MissingKeysReport</c>.</summary>
public sealed record MissingKeysView(ProjectBinding Binding, IReadOnlyList<ConfigEnvironment> Environments, IReadOnlyList<MatrixRow> Rows, int IgnoredCount, DateTimeOffset LoadedAt)
{
    public int GapCount => Rows.Count(r => r.HasGap);
}
