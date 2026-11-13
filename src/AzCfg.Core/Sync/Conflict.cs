using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

public sealed record Conflict(string Key, ConfigEntry? Base, ConfigEntry? Local, ConfigEntry? Remote);
