# UI component guide (Fluent UI Blazor v5.1.0)

Read this before writing UI code. Agents' memory of Fluent UI is mostly v4; **v5 differs**. Everything below was checked
against the package XML docs
(`~/.nuget/packages/microsoft.fluentui.aspnetcore.components/5.1.0/lib/net10.0/*.xml`), the F1 spike and the P1 components. Entries
marked *(XML only)* were read from the XML docs but not rendered in a test yet — verify them when you first use them and fix
this file in the same PR.

Rules of `src/AzCfg.Ui` (plan §9): Fluent components with `Size="…Small"` where the component has a size; colours and spacing only
through Fluent tokens (`var(--colorNeutralBackground1)`, …) or the `--az-*` tokens of `wwwroot/css/app.css` — **no hex colours in
`*.razor*`** (a test greps for them); styles in `*.razor.css`; icons `Icons.Regular.Size16.*` in content, `Size20` in the activity bar.

## 1. Setup (done once, by F1/P1)

```csharp
services.AddFluentUIComponents();                       // IDialogService, INotificationService, IKeyCodeService, IThemeService
services.AddRazorComponents().AddInteractiveServerComponents()
        .AddHubOptions(o => o.MaximumReceiveMessageSize = 2 * 1024 * 1024);
```
`_Imports.razor`: `@using Microsoft.FluentUI.AspNetCore.Components` and `@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons`.
`App.razor`: stylesheet `_content/Microsoft.FluentUI.AspNetCore.Components/Microsoft.FluentUI.AspNetCore.Components.bundle.scp.css`;
the package's `*.lib.module.js` loads itself as a Blazor JS initializer (no `<script>` tag).
`MainLayout.razor`: `<FluentProviders />` (hosts dialogs and toasts) and `<FluentKeyCodeProvider />` (feeds `IKeyCodeService`).

**Do not use v4 names:** `FluentDesignTheme`, `IToastService`, `FluentDesignSystemProvider`, `Appearance.Accent` (v5: `ButtonAppearance.Primary`), `ButtonAppearance.Secondary` (does not exist; use `Outline`).

## 2. Services

### 2.1 `IThemeService` — theme at first render
```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender) await ThemeService.SetThemeAsync(new ThemeSettings("#5B9DFF", 0, 0, ThemeMode.Dark, false));
}
```
The hex lives in `Features/Platform/UiTheme.cs` (`UiTheme.Settings`), not in markup. `SetThemeAsync(ThemeMode.Dark)` also exists.
Other members: `SwitchThemeAsync`, `IsDarkModeAsync`, `GetThemeSettingsAsync`.
bUnit: works with `JSInterop.Mode = Loose`.

### 2.2 `IDialogService`
```csharp
var r = await Dialogs.ShowConfirmationAsync("Message", "Title", "Primary", "Secondary");   // r.Cancelled == true when secondary
```
Use the wrappers of `Components/Common/DialogServiceExtensions.cs`:
```csharp
if (await Dialogs.ConfirmAsync("Delete key", "Delete ‘Db:Host’?", "Delete")) { … }          // bool
if (await Dialogs.ConfirmTypedAsync("Apply to prod", "Apply 3 changes?", "prod")) { … }     // typed confirmation, AD-7
```
`ShowDialogAsync<TDialog>(o => { o.Header.Title = "…"; o.Parameters.Add(nameof(TDialog.Prop), value); })` shows a component as the dialog
body and **returns a `DialogResult` when the dialog closes** (`Cancelled`). `Parameters` keys are the `[Parameter]` property names.
The body component may take `[CascadingParameter] IDialogInstance? Dialog` and call `Dialog.CloseAsync()` (ok) or
`Dialog.CancelAsync()` (cancelled). Other members: `ShowDrawerAsync<T>`, `ShowMessageBoxAsync`, `ShowInfoAsync`/`ShowWarningAsync`/
`ShowErrorAsync`, `CloseAsync(instance, result)`. `DialogOptions`: `Header`, `Footer`, `Modal`, `Width`, `Size`, `PreventDismissOnEscape`.
bUnit: render `<FluentProviders />` in the test, start the dialog without awaiting, `WaitForAssertion` on the provider markup, click
`fluent-button` elements by text (see `TabCloserTests`, `TypedConfirmDialogTests`).

### 2.3 `INotificationService` — toasts (non-intrusive results, §2.9)
```csharp
await Notifications.ShowToastAsync(o => { o.Title = "Applied 3 changes to dev"; o.Intent = ToastIntent.Success; });
```
`ToastIntent`: Success, Info, Warning, Error. Other `ToastOptions`: `Message`, `Lifetime`, `AllowDismiss`, `QuickAction`.
`CloseAllToastsAsync()`. Success/info 5 s, warning 8 s, error until closed (§2.9) — set `Lifetime` accordingly.

### 2.4 `IKeyCodeService` — global shortcuts
```csharp
KeyCodes.RegisterListener(OnKeyAsync);                              // Func<FluentKeyCodeEventArgs, Task>
private async Task OnKeyAsync(FluentKeyCodeEventArgs e) { if (e.Name == "keydown") … }
KeyCodes.UnregisterListener(OnKeyAsync);                            // in Dispose
```
`FluentKeyCodeEventArgs`: `Name` ("keydown"/"keyup"), `Value` (the key text, `"k"`, `"PageDown"`), `Key` (enum), `CtrlKey`, `AltKey`,
`ShiftKey`, `MetaKey`, `TargetId`. The workbench maps them with `KeyChord.From(e)` → `KeyBindings.Resolve` → `ICommandRegistry`.
Features never register their own key listeners: they add a command with a shortcut (§4.5). Needs `<FluentKeyCodeProvider />`.
`PreventDefault`/`PreventDefaultOnly` of the provider work per key code, not per chord, so they cannot stop browser shortcuts such as
`Ctrl+F`; the keymap only avoids the reserved combinations of G31.

## 3. Components

| Component | Minimal snippet | Use / avoid |
| --- | --- | --- |
| `FluentProviders` | `<FluentProviders />` | once, in `MainLayout`. |
| `FluentKeyCodeProvider` | `<FluentKeyCodeProvider />` | once, in `MainLayout`. |
| `FluentLayout` | `<FluentLayout>…</FluentLayout>` *(XML only)* | not used by the workbench (own CSS grid); it has header/footer areas and a mobile hamburger. |
| `FluentMultiSplitter` / `FluentMultiSplitterPane` | `<FluentMultiSplitter Orientation="Orientation.Horizontal" Width="100%" Height="100%"><FluentMultiSplitterPane Size="280px" Min="200px" Max="480px" Collapsed="@c" SizeChanged="OnResize">…</FluentMultiSplitterPane><FluentMultiSplitterPane>…</FluentMultiSplitterPane></FluentMultiSplitter>` | `Size`/`Min`/`Max` are CSS lengths (strings); `SizeChanged` gives the new size string. Sidebar │ main in `Workbench`. |
| `FluentAppBar` / `FluentAppBarItem` | `<FluentAppBar Orientation="Orientation.Vertical"><FluentAppBarItem Text="" Tooltip="Explorer" IconRest="@(new Icons.Regular.Size20.Folder())" OnClick="…" /></FluentAppBar>` | the activity bar. Icons are instances (`new Icons…()`), not render fragments. `Count` shows a counter badge. |
| `FluentButton` | `<FluentButton Appearance="ButtonAppearance.Outline" Size="ButtonSize.Small" OnClick="…">Text</FluentButton>` | `ButtonAppearance`: Default, Outline, Primary, Subtle, Transparent (**no** Secondary/Accent/Lightweight). `ButtonSize`: Small, Medium, Large. `IconOnly="true"` + `Title`; `Loading`; `Disabled`. Renders `<fluent-button>`. |
| `FluentSplitButton` | `<FluentSplitButton OnClick="…" OnMenuClick="…">Apply</FluentSplitButton>` *(XML only)* | primary action + menu (Pull selected / Pull all). |
| `FluentToggleButton` | `<FluentToggleButton Pressed="@on">…</FluentToggleButton>` *(XML only)* | Browse filters (Changed / Secrets / Ignored). |
| `FluentIcon<T>` | `<FluentIcon Value="@(new Icons.Regular.Size16.Key())" Title="Secret" />` | two icons in a conditional need a cast: `open ? (Icon)new …ChevronDown() : new …ChevronRight()`. Names verified: Add, ArrowClockwise, ArrowSync, Checkmark, ChevronDown, ChevronRight, Circle, Cloud, Code, Copy, Cube, Database, Delete, Dismiss, Document, Edit, Eye, EyeOff, Folder, Home, Info, Key, LockClosed, PanelLeft, PanelRight, Person, Pin, Search, Settings, Star, Warning. |
| `FluentMessageBar` | `<FluentMessageBar Intent="MessageBarIntent.Error" Layout="MessageBarLayout.SingleLine" AllowDismiss="false"><ChildContent>Text</ChildContent><ActionsTemplate><FluentButton …>Retry</FluentButton></ActionsTemplate></FluentMessageBar>` | v5 requires the explicit `<ChildContent>` when `ActionsTemplate` is used. `Intent`: Success, Warning, Error, Info. See `ErrorBar`. |
| `FluentErrorBoundary` | `<FluentErrorBoundary><ChildContent>…</ChildContent><ErrorContent>…message bar…</ErrorContent></FluentErrorBoundary>` | `ErrorContent` is a plain `RenderFragment` (no exception parameter); `Recover()` resets it — `TabHost` instead re-keys the boundary (`@key="(tab.Id, generation)"`) to implement "Reload tab". |
| `FluentHighlighter` | `<FluentHighlighter HighlightedText="@text" Text="@name" />` | search-text highlighting in `KeyTreeGrid`; `CaseSensitive` is false by default. |
| `FluentSpinner` | `<FluentSpinner Size="SpinnerSize.Tiny" />` | in buttons / status bar. |
| `FluentProgressBar` | `<FluentProgressBar />` (indeterminate when `Value` is null) *(XML only)* | under the project header while reading. |
| `FluentSkeleton` | `<FluentSkeleton Width="120px" Height="12px" Shimmer="true" />` *(XML only)* | grid placeholder rows. |
| `FluentBadge` | `<FluentBadge Appearance="BadgeAppearance.Tint" Color="BadgeColor.Informative">global</FluentBadge>` *(XML only)* | `global` and label badges; status colours come from `--az-*` instead (`DiffBadge`). |
| `FluentCounterBadge` | `<FluentCounterBadge Count="4" />` *(XML only)* | Sync mode counter. |
| `FluentTooltip` | `<FluentTooltip Anchor="btn-id">text</FluentTooltip>` *(XML only)* | most components also take `Tooltip="…"`; prefer that. |
| `FluentTabs` / `FluentTab` | `<FluentTabs ActiveTabId="@id" ActiveTabIdChanged="…"><FluentTab Id="a" Header="A">…</FluentTab></FluentTabs>` *(XML only)* | **not** the workbench tab strip (that is the own `TabStrip`: dirty dot, lock, middle-click); use inside a view when a simple tab control is needed. |
| `FluentTreeView` / `FluentTreeItem` | `<FluentTreeView LazyLoadItems="…" OnSelectedChanged="…"><FluentTreeItem Text="…" Expanded="…" /></FluentTreeView>` *(XML only)* | workspace tree (U1). Not for keys: `KeyTreeGrid` does that. |
| `FluentDataGrid<T>` | `<FluentDataGrid TGridItem="T" Items="@q" Virtualize="true" ItemSize="28" ItemKey="…">` | `Items` is `IQueryable<T>`; needs a fixed-height scrolling parent. Used for flat tables (Sync rows, directory list). The key tree uses `KeyTreeGrid`. |
| `FluentMenu` / `FluentMenuButton` / `FluentMenuItem` | `<FluentMenu OpenOnContext="true">…<FluentMenuItem OnClick="…">Copy key</FluentMenuItem></FluentMenu>` *(XML only)* | context menus (`OpenOnContext`). `FluentMenuItem` has `IconStart`, `Disabled`, `Checked`. |
| `FluentToast` | via `INotificationService` only | never render it directly. |
| `FluentDialog` / `FluentDialogBody` | via `IDialogService` only (`TitleTemplate`, `ActionTemplate`) | never place in markup; v4 `<FluentDialog>` inline usage is gone. |
| `FluentWizard` / `FluentWizardStep` | `<FluentWizard Value="@step" ValueChanged="…" OnFinish="…"><Steps><FluentWizardStep Label="Mode">…</FluentWizardStep></Steps></FluentWizard>` *(XML only)* | environment wizard (U1). |
| `FluentTextInput` | `<FluentTextInput Value="@v" ValueChanged="…" Placeholder="…" />` *(XML only)* | where an immediate `oninput` is needed (palette, typed confirmation) the platform uses a native `<input class="az-input">`, which is simpler to test. |
| `FluentCombobox` / `FluentSelect` / `FluentAutocomplete` | *(XML only)* | pickers (vault, side of Compare). |
| `FluentCheckbox` | `<FluentCheckbox CheckState="…" CheckStateChanged="…">label</FluentCheckbox>` *(XML only)* | v5 uses `CheckState`, not `Value`. |
| `FluentSwitch` | `<FluentSwitch>include deletions</FluentSwitch>` *(XML only)* | on/off options (Include deletions). |
| `FluentStack` | `<FluentStack Orientation="Orientation.Horizontal" HorizontalGap="8">` *(XML only)* | simple rows. |
| `FluentText` | `<FluentText Size="TextSize.Size200">…</FluentText>` *(XML only)* | typography; `TextSize.Size100…Size1000`. |
| `FluentPopover` | `<FluentPopover AnchorId="btn" @bind-Opened="open">…</FluentPopover>` *(XML only)* | the status-bar outcomes popover is a plain absolutely positioned `div` (`OperationsIndicator`). |
| `FluentOverlay` | `<FluentOverlay @bind-Visible="v" />` *(XML only)* | the palette uses its own backdrop. |

## 4. Platform contracts for features

### 4.1 Register a tab, mode, sidebar section, status item, command
Only in `Features/<X>/<X>Feature.cs` (plan §9):
```csharp
public static IServiceCollection AddBrowseFeature(this IServiceCollection services) => services
    .AddProjectMode(ProjectMode.Browse, typeof(BrowseMode))                   // component takes [Parameter, EditorRequired] ProjectSession Session
    .AddTab(TabKind.Settings, typeof(SettingsTab))                            // component takes [Parameter, EditorRequired] WorkbenchTab Tab
    .AddSidebarSection("workspace", "Workspace", typeof(WorkspaceSection), order: 10)
    .AddStatusBarItem(typeof(SyncSummaryItem), StatusBarAlignment.Left, order: 10)
    .AddCommands<BrowseCommands>();                                           // scoped ICommandContributor
```
A duplicate tab kind, mode or section id throws `InvalidOperationException` at registration. The platform registers the Welcome tab
(`PlatformFeature`).

### 4.2 Tabs
`WorkbenchNavigator.OpenProject/OpenSettings/OpenWelcome` open or activate (dedupe by `TabDescriptor`). Set `TabsState.SetDirty(id, true)`
when a project has pending edits. Close through `TabCloser.CloseAsync(id)` (asks "Discard N pending edits?" for dirty tabs, drops the
project session). `TabHost` keeps loaded tabs alive (hidden) and wraps each in an error boundary with "Reload tab".

### 4.3 Operations and errors
`IOperationRunner.RunAsync(title, kind, work, ct)` then `switch` on `OperationResult<T>.Success/Failed/Cancelled`. `Failed` → `<ErrorBar
Message="@m" Details="@d" OnRetry="…" />` for a view-blocking error, a toast otherwise (§2.9). `AuthBar` shows the workbench sign-in bar by
itself when `IAzureStatusService.Account.State` is `AuthRequired`.

### 4.4 Common components
`EnvPill`/`EnvironmentSwitcher` (take `EnvPillModel`: build it with `EnvironmentRules.KindOf`), `StatusDot`, `DiffBadge` (`Kind`, `HasDraft`),
`SecretValue` (`OnReveal` callback; mask by default; hides after 30 s, on tab change and on dispose; keeps only a `SecretString`),
`InspectorPane` + `InspectorSection`, `ErrorBar`, `EmptyState` (one line + one action), `TypedConfirmDialog`.
`KeyTreeModel<TRow>` (pure) + `KeyTreeGrid<TRow>` (`Model`, `Columns` of `KeyTreeColumn<TRow>(title, width, cell)`, `SelectedPath(Changed)`,
`OnActivate`, `HighlightText`; cells with inputs must stop `keydown` propagation so arrow keys keep working in the input).

### 4.5 Commands and shortcuts
Contribute `UiCommand(id, title, category, shortcut, execute, isEnabled)` through a scoped `ICommandContributor`; use the ids of
`CommandIds`. The shortcut shown in the palette is `KeyBindings.ShortcutOf(id)`; the keymap (`KeyBindings.All`, §2.10) never uses a
browser-reserved combination. A disabled command is shown greyed in the palette and never executed. `ICommandRegistry.Register(command)`
adds a dynamic command and returns an `IDisposable` to remove it.
Palette prefixes: `>` commands, `#` projects, `@` keys of the active project tab (names only — never values), `env:` environments.

### 4.6 Layout
`LayoutState` (scoped): sidebar/inspector visibility and widths. The viewport is only known to CSS: below 1024 px the sidebar and
below 1280 px the inspector are overlay drawers that start closed; `Ctrl+B` / `Alt+I` flip both the wide and the overlay flag.
`InspectorPane` hides itself according to `LayoutState`; features render it next to the grid.

## 5. Testing with bUnit

```csharp
public sealed class MyTests : UiTestContext        // AddFluentUIComponents() + JSInterop.Mode = Loose
{
    [Fact] public void Render_…() { var cut = Render<MyComponent>(p => p.Add(c => c.Value, 1)); cut.Find("fluent-button").Click(); }
}
```
- Fluent renders web components: `fluent-button`, `fluent-message-bar`…; find buttons with `cut.FindAll("fluent-button")` and compare `TextContent.Trim()`.
- Use `WorkbenchContext` (`tests/AzCfg.Ui.Tests/Components/Layout`) for anything that needs the workbench services, fakes of
  `IOperationRunner`/`IAzureStatusService`/`IWorkspaceRegistry`/`IWorkspaceService` and test registrations.
- State changed outside the renderer thread: wrap in `cut.InvokeAsync(...)` and assert with `cut.WaitForAssertion(...)`.
- Timers: register `FakeTimeProvider` as `TimeProvider` and `Advance(...)`.
- `Virtualize` renders a small window in bUnit (no real viewport): assert `rendered < total`, not exact counts.
- Secret sentinel: `AZCFG-TEST-SECRET-7f3a` must never appear in markup before Reveal.
