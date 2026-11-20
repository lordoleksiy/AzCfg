# 0010. UI architecture and hosting

Status: accepted (UI MVP, task D1; plan `docs/ui-mvp-plan.md` §5)

## Context
- AzCfg is a CLI over a UI-agnostic Core (ADR 0003: Core never prompts or prints). The next client is a local UI: a
  dark, compact developer workspace (VS Code / Postman / Infisical look) with Browse / Compare / Sync modes in one
  project tab, a command palette and keyboard-first navigation.
- The UI runs on the developer's machine and handles Key Vault secret values. It must not expose them to other local
  processes, other browser origins or logs, and must not move them across an extra API boundary.
- A workspace holds many projects (`.sln`/`.slnx`); the code has no solution parsing or config-file discovery (G1).
- Several pieces the UI needs live in the CLI today or do not exist: Azure error texts (G9), one shared credential and
  a sign-in check (G10), a dynamic port known only after start (G22). Static registries would be global state and
  break parallel tests (G23).
- The UI must be demonstrable and testable without Azure (G36).
- The Fluent UI Blazor v5 API is new (GA 2026-09-28); agents' knowledge of it is unreliable (G35).

## Decision
### AD-1 Hosting
- Blazor Web App, global `InteractiveServer` render mode with `prerender: false`. Kestrel listens only on
  `127.0.0.1:<--port or 0>`; `AllowedHosts=localhost;127.0.0.1`.
- Per run a 32-byte base64url token. The console prints `http://127.0.0.1:<port>/login?t=<token>` and the browser is
  opened with it. `GET /login?t=` validates the token with `CryptographicOperations.FixedTimeEquals`, signs in with
  ASP.NET Core **cookie authentication** (cookie `azcfg_ui`, HttpOnly, SameSite=Strict, session, Path=/) and
  redirects to `/`.
- A **fallback authorization policy** requires an authenticated user for every endpoint including `/_blazor`; static
  assets are anonymous; unauthenticated requests get 401 (no redirect).
- Response headers:
  - CSP `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:;
    font-src 'self' data:; worker-src 'self' blob:; connect-src 'self' ws://127.0.0.1:*; frame-ancestors 'none'`;
  - `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`.
  - This CSP is the baseline. A relaxation that Monaco or Fluent turns out to need is recorded in this ADR (amendment)
    together with the reason; none is decided now.
- `HubOptions.MaximumReceiveMessageSize = 2 MB`, `DetailedErrors = false`, `Microsoft.AspNetCore` logs at `Warning`
  or above, so request URLs with `?t=` are never logged; own code never logs the token (G21).
- The real URL comes from `IServerAddressesFeature` on `ApplicationStarted` (G22).
- Options: `--port N`, `--no-browser`, `--demo[=empty]`, `--state-root <dir>`.
- Pattern of the .NET Aspire dashboard.

### AD-2 Projects
```
src/AzCfg.Ui.Application   classlib → Core, Infrastructure; pkg Microsoft.Extensions.Hosting.Abstractions.
                           NO Microsoft.AspNetCore.*, Microsoft.JSInterop, Fluent UI (architecture test).
src/AzCfg.Ui               Microsoft.NET.Sdk.Web → Ui.Application; pkgs Fluent UI, Fluent Icons, BlazorMonaco.
tests/AzCfg.Ui.Tests       Microsoft.NET.Sdk.Razor → AzCfg.Ui; xunit, bunit, Mvc.Testing, TimeProvider.Testing.
```
- `AzCfgUiApp.Build(args, overrides)` composes the app; `Program.cs` is one line; tests use
  `WebApplicationFactory<Program>`.
- **Components/Features** render and interact. They inject only Application interfaces, State, `IOperationRunner`,
  `ICommandRegistry`, Fluent `IDialogService`/`INotificationService`/`IKeyCodeService`/`IThemeService` and the
  cross-feature flows. Every IO call goes through `IOperationRunner.RunAsync`.
- **Application** orchestrates Core use cases and ports (the counterpart of the CLI `*CommandBase`): view models,
  masking, UI safety rules (ADR 0011). Plain classes, tested with xunit on **real** Core services + in-memory adapters.
  Exceptions: `ArgumentException` (bad input), `InvalidOperationException` (forbidden state); messages name keys, never
  values.
- **Application never re-implements a Core rule.** Anything that decides sync/apply semantics lives in Core.
- Core/Infrastructure rules of `CLAUDE.md` are unchanged. Core gets the port `IWorkspaceScanner` (solutions, projects
  and well-known config files of a folder; a broken solution file is skipped with a logged warning) — types in Core,
  implementation in Infrastructure (G1).

### AD-3 Components
- Fluent UI Blazor **v5** + Fluent icons.
- Custom components only where Fluent has no equivalent: workbench shell, tab strip, `KeyTreeGrid`, command palette,
  `EnvPill`/`EnvironmentSwitcher`, `SecretValue`, Monaco wrappers.
- No other UI library, no own JS except what BlazorMonaco needs.
- Fluent usage patterns are taken from the package XML docs into `docs/ui-components.md`, not from memory (G35).

### AD-4 State
- Scoped (per circuit) observable state with `event Action? Changed`: `WorkspaceState`, `TabsState`,
  `ProjectSessions`/`ProjectSession`, `ProtectedUnlockState`. No Fluxor.
- `ProjectSession` caches the Browse view, the comparison and the drafts of one project tab. Caches are invalidated
  after pull / apply / local edit / profile change; drafts survive invalidation.
- Several browser tabs = independent circuits.
- Components subscribe to state and singleton events with `() => InvokeAsync(StateHasChanged)` and unsubscribe in
  `Dispose` (G29).

### AD-9 Long operations
- `IOperationRunner` (singleton): `Running`, `Recent` (last 20), exceptions mapped via `AzureErrors`.
- **Read** operations are cancelled by the component's token.
- **Write** operations (apply, pull, local edit, bind, save profile) ignore the UI token and run with
  `ApplicationStopping`: closing a tab cannot cause a partial apply. Action buttons are disabled while one runs; the
  outcome stays in `Recent` even if the circuit is gone.

### AD-10 Extensibility
- Feature modules register themselves via DI records, not static registries (G23): `TabRegistration`,
  `ProjectModeRegistration`, `SidebarSectionRegistration`, `StatusBarItemRegistration`, `ICommandContributor`.
- Each feature has `Features/<X>/<X>Feature.cs`.
- Cross-feature calls go through small interfaces (`IActivationFlow`, `IBindingFlow`) with no-op defaults (`TryAdd`)
  replaced by the owning feature.
- The workbench renders registered parts via `DynamicComponent`.

### AD-11 Monaco
- `BlazorMonaco` (assets from the package, no CDN), wrapped by `CodeEditor`/`DiffEditor`; a switch to own interop
  touches only these two wrappers (R5).
- Theme `azcfg-dark` matches the UI visual language. Schema `wwwroot/schemas/edit-json.schema.json`.
- Document limit 1 MB.

### AD-12 Demo and test seams
- In-memory adapters live in `AzCfg.Infrastructure.InMemory` with `AddAzCfgInMemory(world)` (G36).
- `--demo` seeds a temporary sample workspace and an in-memory Azure.
- `UiHostOptions.StateRoot` keeps UI files out of the real user state in demo and tests.

### AD-13 Errors
- `AzureErrors.Classify/Describe` (Infrastructure, moved from the CLI; `CliErrors` delegates, G9) →
  `OperationErrorKind` + message (Application) → message bar or toast (UI).
- Authentication errors raise the workbench "sign in" bar. One `TokenCredential` singleton
  (`DefaultAzureCredential`) is shared, and `AzureStatusService` probes a token (G10).

### Alternatives considered
| Topic | Chosen | Rejected (why) |
| --- | --- | --- |
| Hosting | Blazor Server on loopback (one .NET stack, secrets stay server-side, runs anywhere, Aspire precedent) | MAUI Blazor Hybrid (no Linux, workload install); Photino.Blazor (small community — can wrap the same components later); Avalonia (full rewrite, no Monaco); SPA + API (two stacks, secrets cross an API) |
| Components | Fluent UI Blazor v5 (Microsoft, VS Code/Azure look, dialogs/toasts/grid/splitters/icons/keys/theme) | Hand-made kit (~25 components to build and test); MudBlazor (Material look, less IDE-like); Radzen (heavier, not the target look) |
| State | Scoped observable containers | Fluxor (boilerplate for a single-user local app) |
| Editor | BlazorMonaco | Own Monaco interop (fallback only); CodeMirror (no equivalent diff editor) |
| Stale edits | Core 3-way with `BaseJson` (ADR 0011) | UI re-export comparison (detects but cannot merge) |
| Tests | xunit + bUnit + `WebApplicationFactory` + demo world | Playwright (browser install, slow — next iteration, cheap thanks to `--demo`) |

## Consequences
- One .NET stack: the UI reuses Core use cases and Infrastructure adapters as the CLI does; secret values stay in the
  server process and reach the browser only after an explicit Reveal (ADR 0011).
- The UI is reachable only from the local machine and only with the per-run token; another local process or origin
  cannot open the circuit without it.
- `AzCfg.Ui.Application` is testable without ASP.NET Core; an architecture test enforces its references.
- Features are added by registration, not by editing the shell; parallel tests do not share global state.
- Closing a browser tab never interrupts a write; its result remains visible in the operation history.
- Monaco can be swapped for own interop by touching two wrappers.
- The whole UI runs without Azure (`--demo`), which also makes browser end-to-end tests cheap later.
- The CSP may need documented relaxations once Monaco and Fluent are integrated.
