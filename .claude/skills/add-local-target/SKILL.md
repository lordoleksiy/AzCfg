---
name: add-local-target
description: Add a new ILocalTarget (local config file target such as local.settings.json or user-secrets) with a test and DI registration. Use when asked to support a new local config destination.
---

# Add a local target

Contract: `ILocalTarget` in `src/AzCfg.Core/Abstractions/ILocalTarget.cs`:
```csharp
public interface ILocalTarget
{
    LocalTargetKind Kind { get; }
    bool CanHandle(ProjectInfo project);
    Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct);
    Task<IReadOnlyList<LocalValue>> ReadAsync(LocalTargetBinding target, CancellationToken ct);
    Task WriteAsync(LocalTargetBinding target, IReadOnlyList<LocalValue> values, CancellationToken ct);
}
```
`LocalTargetBinding(Kind, ProjectPath, Format)` carries the `.csproj` path and the file's serialization
format (`LocalFileFormat`); `LocalValue(Key, Value)` is a single key/value pair, `Key` without the scope
prefix. Spec: `docs/architecture.md`, ADR 0006.

Files to touch:
1. `src/AzCfg.Infrastructure/Local/<Name>Target.cs` — one public sealed class implementing `ILocalTarget`. All file IO lives here, never in Core.
2. `tests/AzCfg.Infrastructure.Tests/Local/<Name>TargetTests.cs` — write first; use a temp directory, cover `CanHandle`, `InspectAsync`, read, write.
3. `src/AzCfg.Infrastructure/DependencyInjection.cs` — register: `services.AddSingleton<ILocalTarget, <Name>Target>();`

Rules:
- Format is per-target, not per-kind assumption: `LocalFileFormat.NestedJson` for user-secrets and
  `appsettings.local.json` (segments of the key path, split on `:`, become nested JSON objects, no scope
  prefix); `LocalFileFormat.FlatDictionary` is the only format for `local.settings.json` — a flat string
  dictionary under `Values`, keys keep their full `A:B` path as-is, never nested.
- Secrets must never be written to git-tracked files (check `.gitignore` coverage); user-secrets is the
  secret target outside the repo.
- `InspectAsync` must report `IsSafeForSecrets` correctly: `true` for user-secrets; for a file inside the
  repo, `true` only if it is git-ignored and not tracked.
- Finish with `dotnet build` and `dotnet test`.
