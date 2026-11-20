namespace AzCfg.Ui.Application.Environments;

public enum ConnectionState
{
    Unknown,
    Checking,
    Connected,
    AuthRequired,
    Forbidden,
    Unreachable,
}
