namespace AzCfg.Ui.Application.Configuration;

public sealed record CompareRow(string Key, CompareResult Result, CompareCell? Left, CompareCell? Right);
