# AzCfg — локальний менеджер Azure App Configuration + Key Vault

Інструмент для розробників: двостороння синхронізація конфігів між Azure App Configuration / Key Vault
та локальними файлами (.NET проєкти), плюс редагування App Configuration у форматі JSON.

## Ключові рішення (узгоджено)

### Продукт
- **Zero-config**: жодних обовʼязкових файлів у репо. Розробник один раз проходить майстер `azcfg init`,
  далі просто `azcfg pull / edit / push`.
- Опційний `.azcfg.yaml` у репо лише як escape hatch — перекриває автовизначення.
- Розробники мають право змінювати App Configuration напряму → push іде прямо в Azure (не через pipeline).
- Стек: **.NET**. Core — class library; клієнт зараз — CLI (`dotnet tool`), у майбутньому UI.

### Конвенція ключів компанії
- Сервісні ключі: `<service-name>:<Key:Path>`, напр. `crm-api-service:ConnectionStrings:Crm`.
- Глобальні: `global:<Key:Path>`.
- Pull: беремо `global:*` + `<service>:*`, зрізаємо префікс, сервісні перекривають global (з provenance).
- Push: за замовчуванням scope = сервіс; зміна `global` — лише явно (секція `@global`) + окреме попередження.
- Назви секретів KV: `:` → `--` (напр. `crm-api-service--ConnectionStrings--Crm`).
- Формат — властивість локальної цілі (`LocalTargetBinding.Format`, ADR 0006): `NestedJson` для user-secrets і
  `appsettings.local.json` (`Dataverse:ConnString` → `{"Dataverse":{"ConnString":"..."}}`, без scope-префікса);
  `FlatDictionary` — єдиний дозволений формат для `local.settings.json` (`Values` — плоский словник рядків,
  ключі `A:B` як є, без вкладеності; вкладений JSON для цієї цілі заборонено). `local.settings.json` спроєктовано в
  контрактах, реалізація Infrastructure — пізніше (див. ADR 0004, ADR 0006).

### Середовища
- Зараз: **окремий App Configuration на кожне середовище** (dev / stage / prod).
- Архітектурно закладено дві стратегії через `IEnvironmentResolver`:
  `SeparateStoresResolver` (реалізуємо зараз, Label = null) і `LabelResolver` (пізніше).
- `ConfigEnvironment(Name, StoreEndpoint, Label?)` — усі use cases працюють лише з ним.

### Автовизначення (Discovery)
- **App Config stores**: Azure Resource Graph (`Microsoft.AppConfiguration/configurationStores`) по всіх
  підписках; мапінг на середовища за тегом `environment`, потім за суфіксом назви (`-dev`, `-stg`, `-prod`).
  Fallback — ручне введення endpoint.
- **Сервіс проєкту**: реальні префікси з dev store (частина до першої `:`), зіставлені з назвою .csproj,
  папки, git remote, наявними ключами local.settings.json. Впевнений збіг — автоматично, інакше вибір. Запамʼятовується.
- **Цілі**: розробник сам вирішує, куди йдуть значення, а куди — секрети (ADR 0006); Core лише пропонує
  варіанти (`LocalTargetOption`), рішення не приймає. Варіанти: user-secrets і `appsettings.local.json`
  (MVP, вкладений JSON) та `local.settings.json` (Azure Functions; лише плоский `Values`, спроєктовано, не в
  MVP). Вибір запам'ятовується в `projects.json` (`ProjectBinding.ValuesTarget`/`SecretsTarget`) — наступного
  разу `ProjectResolution.Existing` повертає його без повторних запитань.
- **Key Vault — без хардкоду**:
  - читання: vault береться з URI в самому KV reference (`{"uri":"https://xxx.vault.azure.net/secrets/name"}`);
  - новий секрет: (1) вибір, збережений для проєкту/середовища → (2) найчастіший vault серед референсів сервісу →
    (3) серед `global:` референсів → (4) список з Resource Graph, ранжований (та сама RG, суфікс середовища), вибір
    користувача → (5) у майбутньому — створення vault.

### Стан користувача (не в репо)
`%APPDATA%/azcfg/` (Linux/macOS: `~/.config/azcfg/`):
- `profile.json` — середовища → endpoints;
- `projects.json` — repo path / git remote → сервіс, цілі, обраний vault;
- `snapshots/` — бази для 3-way diff (per repo + env);
- `cache/` — discovery, списки префіксів.

### Синхронізація
- Двостороння: **pull** — remote → local; **push** — local файли → remote (бібліотечний дефолт,
  `PushSource.LocalFiles`); **edit** — це push із джерелом `PushSource.EditedJson(json)`. Модель як
  Terraform: **plan → apply**.
- Один конвеєр для всіх напрямків: кожна сторона (remote, локальні файли, база з останнього pull)
  нормалізується в `ConfigState`, далі — 3-way diff (`ConfigDiff`: snapshot × локальний стан × поточний
  remote → `DiffItem` із `DiffKind`), далі — `SyncPlan` для застосування. `ISyncService.DiffAsync` віддає
  `ConfigDiff` напряму — цим користується майбутній UI для порівняння local/remote/base без обов'язкового
  plan/apply.
- Нормалізація (деталі — ADR 0007): значення або `StateValue.Plain`, або `StateValue.Secret(SecretUri?, Fingerprint)`
  — секрет ніколи не несе сам текст, лише fingerprint. Локальний ключ — секрет лише якщо база/remote вже
  знають його як KV-референс; інакше `Plain` (нові секрети вводяться лише через `@kv` у edit JSON). Правило
  scope для push: локальна зміна ключа, що прийшов з `global`, створює override у scope сервісу (global
  ніколи не перезаписується мовчки); локальне видалення ключа з `global` нічого не видаляє remote; `global`
  змінюється лише явно, через `@global`.
- Запис в App Config з `If-Match: etag`.
- prod: apply вимагає ввести назву середовища.
- Секрети ніколи не пишуться у файли, що трекаються git (перевірка `.gitignore`); знімки (`snapshots/`)
  теж не містять значень секретів — лише fingerprint (`SecretString`, `ISecretFingerprinter`, ADR 0007).
- Auth: тільки `DefaultAzureCredential`. RBAC: App Configuration Data Owner; Key Vault Secrets User / Officer.

### JSON-формат редагування
```json
{
  "ConnectionStrings": {
    "Crm": { "@kv": "crm-api-service--ConnectionStrings--Crm" }
  },
  "Features": { "UseNewPricing": true },
  "@global": { "Logging": { "Level": "Information" } }
}
```
- `{ "@kv": "name" }` → KV reference (content type `application/vnd.microsoft.appconfig.keyvaultref+json`).
- `{ "@kv": "name", "value": "..." }` → створити/оновити секрет у KV, потім reference.
- Вкладеність → `:`, додається префікс сервісу (або `global` для `@global`).

## Архітектура

**Головне правило: Core нічого не питає і не друкує.** Він повертає дані (пропозиції, плани), рішення приймає клієнт.
Так CLI, `--json` режим і майбутній UI працюють через той самий API.

```
src/
  AzCfg.Core/             чиста логіка, БЕЗ Azure SDK і файлової системи
    Model/                ConfigEntry, ConfigValue, ConfigEnvironment, ConfigScope, LocalTargetKind,
                          LocalFileFormat, LocalTargetBinding, LocalValue, ProjectBinding, SecretString
    Mapping/              KeyMapper, JsonProjection (@kv, @global), ScopeMerger
    Sync/                 PushSource, StateValue, StateItem, ConfigState, DiffKind, DiffItem, ConfigDiff,
                          ThreeWayDiff, SyncPlan, Change, Conflict
    Security/             HmacSecretFingerprinter
    Abstractions/         IConfigStore, ISecretStore, IResourceDiscovery, ILocalTarget,
                          IEnvironmentResolver, IUserStateStore, ISecretFingerprinter
    UseCases/             SetupService, ProjectService, SyncService, LocalTargetOption
  AzCfg.Infrastructure/
    Azure/                AppConfigStore, KeyVaultSecretStore, ResourceGraphDiscovery
    Local/                UserSecretsTarget, AppSettingsLocalTarget, LocalSettingsTarget (пізніше), CsprojDetector
    State/                JsonUserStateStore
    DependencyInjection.cs   services.AddAzCfg()
  AzCfg.Cli/              Spectre.Console.Cli — тільки взаємодія з користувачем
tests/
  AzCfg.Core.Tests/       основна маса тестів (маппінг, merge, diff)
  AzCfg.Infrastructure.Tests/
```

### Модель
`ConfigEntry.Key`, `StateItem.Key`, `DiffItem.Key`, `LocalValue.Key` — конфігураційний шлях **без**
scope-префікса (напр. `ConnectionStrings:Crm`); ключ стору будується через `KeyMapper.ToStoreKey(Scope, service, Key)`.

```csharp
public enum ConfigScope { Global, Service }

public abstract record ConfigValue
{
    public sealed record Plain(string Value) : ConfigValue;
    public sealed record KeyVaultRef(Uri SecretUri) : ConfigValue;
    public sealed record ResolvedSecret(Uri SecretUri, SecretString Value) : ConfigValue;
}

public sealed record ConfigEntry(string Key, ConfigScope Scope, ConfigValue Value, string? ETag = null);
public sealed record ConfigEnvironment(string Name, Uri StoreEndpoint, string? Label);

// decision 1: локальні цілі — розробник обирає, Core лише пропонує варіанти
public enum LocalTargetKind { UserSecrets, AppSettingsLocal, LocalSettings }
public enum LocalFileFormat { NestedJson, FlatDictionary }
public sealed record LocalTargetBinding(LocalTargetKind Kind, string ProjectPath, LocalFileFormat Format);
// LocalSettings вимагає FlatDictionary — валідується в конструкторі.

// decision 2: прив'язка памʼятає вибір значень/секретів окремо (можуть збігатися)
public sealed record ProjectBinding(
    string RepoPath, string Service,
    LocalTargetBinding ValuesTarget, LocalTargetBinding SecretsTarget,
    IReadOnlyDictionary<string, Uri> VaultByEnvironment);

// decision 4: пара ключ/значення локального файлу; Value не потрапляє у ToString (може бути секрет)
public sealed record LocalValue(string Key, string Value);

// decision 5: секрет ніколи не друкується/серіалізується як є
public sealed class SecretString
{
    public string Reveal();           // явний доступ до значення
    public override string ToString(); // "***"
}
```

### Порти
```csharp
public interface IConfigStore
{
    IAsyncEnumerable<ConfigEntry> ReadAsync(ConfigEnvironment env, string service, CancellationToken ct);
    IAsyncEnumerable<string> ListServicePrefixesAsync(ConfigEnvironment env, CancellationToken ct);
    Task<ApplyResult> ApplyAsync(ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct);
}
public interface ISecretStore
{
    Task<string> GetAsync(Uri secretUri, CancellationToken ct);
    Task<Uri> SetAsync(Uri vaultUri, string name, string value, CancellationToken ct);
}
public interface IResourceDiscovery
{
    Task<IReadOnlyList<AzureResource>> FindAppConfigStoresAsync(CancellationToken ct);
    Task<IReadOnlyList<AzureResource>> FindKeyVaultsAsync(CancellationToken ct);
}
public interface ILocalTarget
{
    LocalTargetKind Kind { get; }
    bool CanHandle(ProjectInfo project);
    Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct);
    Task<IReadOnlyList<LocalValue>> ReadAsync(LocalTargetBinding target, CancellationToken ct);
    Task WriteAsync(LocalTargetBinding target, IReadOnlyList<LocalValue> values, CancellationToken ct);
}
public interface IEnvironmentResolver { IReadOnlyList<ConfigEnvironment> Resolve(UserProfile profile); }

// decision 5: знімки не містять значень секретів — лише fingerprint у ConfigState
public interface IUserStateStore
{
    Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct);
    Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct);
    Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct); // персональний ключ, не в репо
    // + LoadProfileAsync/SaveProfileAsync, LoadBindingAsync/SaveBindingAsync
}
public interface ISecretFingerprinter { string Fingerprint(SecretString secret); } // HmacSecretFingerprinter
```

### Use cases: «пропозиція → рішення»
```csharp
ProjectResolution r = await projects.ResolveAsync(repoPath, ct);
// r.Existing != null → клієнт використовує запамʼятований ProjectBinding без запитань;
// інакше клієнт обирає з r.TargetOptions (LocalTargetOption: Target, Exists, IsSafeForSecrets).
ProjectBinding b = await projects.BindAsync(repoPath, service, valuesTarget, secretsTarget, ct);

// push: бібліотечний дефолт — з локальних файлів; edit — push відредагованого JSON.
SyncPlan plan = await sync.PlanPushAsync(b, env, new PushSource.LocalFiles(), ct);
SyncPlan editPlan = await sync.PlanPushAsync(b, env, new PushSource.EditedJson(json), ct);

// дані для майбутнього UI (порівняння local/remote/base), окремо від plan/apply:
ConfigDiff diff = await sync.DiffAsync(b, env, ct);

ApplyResult res = await sync.ApplyAsync(plan, new ApplyOptions(ConfirmedEnvironment: "prod"), ct);
```
- Прогрес: `IProgress<SyncProgress>`; логування: `ILogger<T>`.
- `SyncPlan` — record: CLI малює таблицю, `--json` серіалізує як є, UI показує diff.

### Пакети
`Azure.Identity`, `Azure.Data.AppConfiguration`, `Azure.Security.KeyVault.Secrets`,
`Azure.ResourceManager.ResourceGraph`, `Spectre.Console.Cli`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`.

## CLI
```
azcfg init                     майстер середовищ
azcfg pull   [--env dev]       App Config + KV → локальні файли
azcfg edit   [--env dev]       JSON у $EDITOR → plan → підтвердження → push
azcfg plan / push
azcfg diff dev stage           порівняння середовищ
azcfg doctor                   доступи, .gitignore, мертві KV-референси
Глобальні: --env, --json, --yes, --project <path>
```
`--json` — з першого дня (база для VS Code extension).

## Порядок робіт (MVP)
1. Core: модель, `KeyMapper`, `ScopeMerger`, `JsonProjection` + тести.
2. Infrastructure: `AppConfigStore` (read), `KeyVaultSecretStore` (read), `ResourceGraphDiscovery`, `JsonUserStateStore`.
3. `CsprojDetector` + `UserSecretsTarget` + `AppSettingsLocalTarget`.
4. CLI: `init` + `pull` — перша корисна версія.
5. `ThreeWayDiff`, `plan`/`push`/`edit`, далі `CreateSecret` і вибір vault.

## Майбутнє
- `LabelResolver` (одне сховище, labels для середовищ).
- Створення Key Vault з інструменту.
- UI: VS Code extension через `azcfg --json` або локальний `azcfg serve`; можливий десктоп-хост над тим самим Core.
