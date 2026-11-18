using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

public sealed record ComparisonView(ProjectBinding Binding, CompareSide Left, CompareSide Right, IReadOnlyList<CompareRow> Rows,
    int IgnoredCount, string LeftJson, string RightJson, DateTimeOffset LoadedAt);
