using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

public enum ChangeKind { Add, Update, Delete, CreateSecret }

public sealed record Change(ChangeKind Kind, ConfigEntry? Before, ConfigEntry? After, string? SecretName = null, string? SecretValue = null);
