using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>
/// Immediate local edits (AD-5): reads the target(s), changes one key and rewrites only the target(s) that changed. The key's
/// scope marker is kept; a secret is written only to a secrets target that is safe for secrets.
/// </summary>
internal sealed class LocalValuesService(LocalTargetAccess access) : ILocalValuesService
{
    public Task SetPlainAsync(ProjectBinding binding, string key, string value, CancellationToken ct) =>
        SetAsync(binding, key, value, toSecrets: false, ct);

    public async Task SetSecretAsync(ProjectBinding binding, string key, SecretString value, CancellationToken ct)
    {
        var option = await access.For(binding.SecretsTarget).InspectAsync(binding.SecretsTarget, ct);
        if (!option.IsSafeForSecrets)
        {
            throw new InvalidOperationException($"{binding.SecretsTarget.Kind} target is not safe for secrets.");
        }

        await SetAsync(binding, key, value.Reveal(), toSecrets: true, ct);
    }

    public async Task RemoveAsync(ProjectBinding binding, string key, CancellationToken ct)
    {
        var files = await ReadAsync(binding, ct);
        foreach (var file in files.Where(f => f.Values.RemoveAll(v => v.Key == key) > 0))
        {
            await Write(file, ct);
        }
    }

    public async Task SetScopeAsync(ProjectBinding binding, string key, ConfigScope scope, CancellationToken ct)
    {
        var files = await ReadAsync(binding, ct);
        var file = files.FirstOrDefault(f => f.Values.Exists(v => v.Key == key))
            ?? throw new InvalidOperationException($"Key '{key}' is not in a local target.");
        file.Values[file.Values.FindIndex(v => v.Key == key)] = file.Values.First(v => v.Key == key) with { Scope = scope };
        await Write(file, ct);
    }

    private async Task SetAsync(ProjectBinding binding, string key, string value, bool toSecrets, CancellationToken ct)
    {
        var files = await ReadAsync(binding, ct);
        var destination = toSecrets ? files[^1] : files[0];
        var scope = files.SelectMany(f => f.Values).FirstOrDefault(v => v.Key == key)?.Scope ?? ConfigScope.Service;
        var changed = files.Where(f => f != destination && f.Values.RemoveAll(v => v.Key == key) > 0).ToList();
        var index = destination.Values.FindIndex(v => v.Key == key);
        var entry = new LocalValue(key, value, scope);
        if (index >= 0)
        {
            destination.Values[index] = entry;
        }
        else
        {
            destination.Values.Add(entry);
        }

        foreach (var file in changed.Append(destination))
        {
            await Write(file, ct);
        }
    }

    /// <summary>Values file first, secrets file last (the same single file when the targets are shared).</summary>
    private async Task<List<TargetFile>> ReadAsync(ProjectBinding binding, CancellationToken ct)
    {
        var values = await access.For(binding.ValuesTarget).ReadAsync(binding.ValuesTarget, ct);
        List<TargetFile> files = [new(binding.ValuesTarget, [.. values])];
        if (binding.ValuesTarget != binding.SecretsTarget)
        {
            files.Add(new(binding.SecretsTarget, [.. await access.For(binding.SecretsTarget).ReadAsync(binding.SecretsTarget, ct)]));
        }

        return files;
    }

    private Task Write(TargetFile file, CancellationToken ct) => access.For(file.Target).WriteAsync(file.Target, file.Values, ct);

    private sealed record TargetFile(LocalTargetBinding Target, List<LocalValue> Values);
}
