namespace AzCfg.Ui.Application.Environments;

public sealed record AzureAccountStatus(ConnectionState State, string? Account, string? TenantId, string? Message);
