using AzCfg.Core.Model;

namespace AzCfg.Core.Abstractions;

public interface IEnvironmentResolver { IReadOnlyList<ConfigEnvironment> Resolve(UserProfile profile); }
