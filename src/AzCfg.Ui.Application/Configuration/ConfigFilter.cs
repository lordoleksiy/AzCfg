using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

public sealed record ConfigFilter(string? Text = null, bool ChangedOnly = false, bool SecretsOnly = false, bool IgnoredOnly = false, ConfigScope? Scope = null);
