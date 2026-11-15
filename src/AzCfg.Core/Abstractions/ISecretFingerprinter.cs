using AzCfg.Core.Model;

namespace AzCfg.Core.Abstractions;

/// <summary>Computes a non-reversible fingerprint of a secret value, used for change detection in snapshots.</summary>
public interface ISecretFingerprinter
{
    string Fingerprint(SecretString secret);
}
