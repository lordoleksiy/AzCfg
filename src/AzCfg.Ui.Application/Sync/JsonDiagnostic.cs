namespace AzCfg.Ui.Application.Sync;

/// <summary>A problem of a draft's JSON; <see cref="Message"/> names keys only, never values.</summary>
public sealed record JsonDiagnostic(string Message, string? Key, int? Line, int? Column);
