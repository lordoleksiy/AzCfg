---
name: add-local-target
description: Add a new ILocalTarget (local config file target such as local.settings.json or user-secrets) with a test and DI registration. Use when asked to support a new local config destination.
---

# Add a local target

Contract: `ILocalTarget` in `src/AzCfg.Core/Abstractions/ILocalTarget.cs` (`Kind`, `CanHandle`, `ReadAsync`, `WriteAsync`). Spec: `docs/architecture.md`.

Files to touch:
1. `src/AzCfg.Infrastructure/Local/<Name>Target.cs` — one public sealed class implementing `ILocalTarget`. All file IO lives here, never in Core.
2. `tests/AzCfg.Infrastructure.Tests/Local/<Name>TargetTests.cs` — write first; use a temp directory, cover `CanHandle`, read, write, key-format mapping.
3. `src/AzCfg.Infrastructure/DependencyInjection.cs` — register: `services.AddSingleton<ILocalTarget, <Name>Target>();`

Rules:
- Secrets must never be written to git-tracked files (check `.gitignore` coverage); user-secrets is the secret target.
- Key format: Functions `local.settings.json` uses `__` instead of `:`.
- Finish with `dotnet build` and `dotnet test`.
