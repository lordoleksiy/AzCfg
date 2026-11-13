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
- local.settings.json (Functions): `:` → `__`.

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
- **Цілі**: скан `*.csproj` — `AzureFunctionsVersion`/Functions SDK → `local.settings.json` (`Values`);
  `UserSecretsId` → user-secrets (секрети) + appsettings.Development.json; немає — запропонувати `dotnet user-secrets init`.
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
- Модель як Terraform: **plan → apply**.
- 3-way diff: snapshot (база з останнього pull) × локальний стан × поточний remote → зміни + конфлікти.
- Запис в App Config з `If-Match: etag`.
- prod: apply вимагає ввести назву середовища.
- Секрети ніколи не пишуться у файли, що трекаються git (перевірка `.gitignore`).
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
    Model/                ConfigEntry, ConfigValue, ConfigEnvironment, ConfigScope
    Mapping/              KeyMapper, JsonProjection (@kv, @global), ScopeMerger
    Sync/                 ThreeWayDiff, SyncPlan, Change, Conflict
    Abstractions/         IConfigStore, ISecretStore, IResourceDiscovery, ILocalTarget,
                          IEnvironmentResolver, IUserStateStore
    UseCases/             SetupService, ProjectService, SyncService
  AzCfg.Infrastructure/
    Azure/                AppConfigStore, KeyVaultSecretStore, ResourceGraphDiscovery
    Local/                LocalSettingsTarget, UserSecretsTarget, AppSettingsTarget, CsprojDetector
    State/                JsonUserStateStore
    DependencyInjection.cs   services.AddAzCfg()
  AzCfg.Cli/              Spectre.Console.Cli — тільки взаємодія з користувачем
tests/
  AzCfg.Core.Tests/       основна маса тестів (маппінг, merge, diff)
  AzCfg.Infrastructure.Tests/
```

### Модель
```csharp
public enum ConfigScope { Global, Service }

public abstract record ConfigValue
{
    public sealed record Plain(string Value) : ConfigValue;
    public sealed record KeyVaultRef(Uri SecretUri) : ConfigValue;
    public sealed record ResolvedSecret(Uri SecretUri, string Value) : ConfigValue;
}

public sealed record ConfigEntry(string Key, ConfigScope Scope, ConfigValue Value, string? ETag = null);
public sealed record ConfigEnvironment(string Name, Uri StoreEndpoint, string? Label);
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
    string Kind { get; }
    bool CanHandle(ProjectInfo project);
    Task<IReadOnlyDictionary<string, string>> ReadAsync(ProjectInfo project, CancellationToken ct);
    Task WriteAsync(ProjectInfo project, IReadOnlyList<ConfigEntry> entries, CancellationToken ct);
}
public interface IEnvironmentResolver { IReadOnlyList<ConfigEnvironment> Resolve(UserProfile profile); }
```

### Use cases: «пропозиція → рішення»
```csharp
ProjectResolution r = await projects.ResolveAsync(repoPath, ct);      // Candidates, IsConfident, Targets
ProjectBinding b    = await projects.BindAsync(repoPath, service, targets, ct);
SyncPlan plan       = await sync.PlanPushAsync(b, env, editedJson, ct); // Changes, Conflicts, RequiresVaultChoice
ApplyResult res     = await sync.ApplyAsync(plan, new ApplyOptions(ConfirmedEnvironment: "prod"), ct);
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
3. `CsprojDetector` + `LocalSettingsTarget` + `UserSecretsTarget`.
4. CLI: `init` + `pull` — перша корисна версія.
5. `ThreeWayDiff`, `plan`/`push`/`edit`, далі `CreateSecret` і вибір vault.

## Майбутнє
- `LabelResolver` (одне сховище, labels для середовищ).
- Створення Key Vault з інструменту.
- UI: VS Code extension через `azcfg --json` або локальний `azcfg serve`; можливий десктоп-хост над тим самим Core.
