# AzCfg — локальний менеджер Azure App Configuration + Key Vault

Інструмент для розробників: двостороння синхронізація конфігів між Azure App Configuration / Key Vault
та локальними файлами (.NET проєкти), плюс редагування App Configuration у форматі JSON.

## Ключові рішення (узгоджено)

### Продукт
- **Zero-config**: жодних обовʼязкових файлів у репо. Розробник один раз проходить майстер `azcfg init`,
  далі просто `azcfg pull / edit / push`.
- Опційний маніфест проєкту `azcfg.json` у репо (ADR 0012) — описує структуру конфігурації й перекриває
  автовизначення; замінює нереалізований `.azcfg.yaml`. Без маніфесту zero-config працює як раніше.
- Розробники мають право змінювати App Configuration напряму → push іде прямо в Azure (не через pipeline).
- Стек: **.NET**. Core — class library; клієнт зараз — CLI (`dotnet tool`), у майбутньому UI.

### Конвенція ключів компанії
- Сервісні ключі: `<service-name>:<Key:Path>`, напр. `crm-api-service:ConnectionStrings:Crm`.
- Глобальні: `global:<Key:Path>`.
- Pull: беремо `global:*` + `<service>:*`, зрізаємо префікс, сервісні перекривають global (з provenance).
- Push: за замовчуванням scope = сервіс; зміна `global` — лише явно (секція `@global`) + окреме попередження.
- Назви секретів KV: `:` → `--` (напр. `crm-api-service--ConnectionStrings--Crm`).
- Global-ключі в локальних JSON-файлах позначаються коментарем `//@global` у кінці рядка (ADR 0009):
  `LocalValue.Scope = Global`, push відносить такий ключ до `global` (з підтвердженням `TouchesGlobal`).
- Формат — властивість локальної цілі (`LocalTargetBinding.Format`, ADR 0006): `NestedJson` для user-secrets і
  `appsettings.local.json` (`Dataverse:ConnString` → `{"Dataverse":{"ConnString":"..."}}`, без scope-префікса);
  `FlatDictionary` — єдиний дозволений формат для `local.settings.json` (`Values` — плоский словник рядків,
  ключі `A:B` як є, без вкладеності; вкладений JSON для цієї цілі заборонено). `local.settings.json` спроєктовано в
  контрактах, реалізація Infrastructure — пізніше (див. ADR 0004, ADR 0006).

### Середовища (ADR 0002, ADR 0008)
- Два режими, обираються в `azcfg init --mode stores|labels` і зберігаються в профілі (`UserProfile.Mode`):
  - **окремий App Configuration на кожне середовище** (`StorePerEnvironment`, дефолт) — читаються **усі** labels
    store-а; дублікати ключа під різними labels → одна копія (`WriteLabel` > без label > перший за ordinal) +
    `SyncWarning(DuplicateLabels)`; нові ключі отримують `WriteLabel` (label середовища в профілі або `DefaultLabel`);
  - **один store, label на середовище** (`LabelPerEnvironment`) — читаються ключі без label + label середовища
    (label перемагає); запис лише з label середовища; ключ під іншим label / без label не змінюється — замість
    цього `Add` override + `SyncWarning(LabelOverride)`.
- Стратегії `IEnvironmentResolver`: `SeparateStoresResolver`, `LabelResolver`; `ProfileEnvironmentResolver` (DI)
  обирає за `profile.Mode`.
- `ConfigEnvironment(Name, StoreEndpoint, Label, IsProtected, WriteLabel)` — усі use cases працюють лише з ним.
  Після resolver-а `Label` — селектор читання (`null` = усі labels), `WriteLabel` — label для нових ключів.

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
  разу `ProjectResolution.Existing` повертає його без повторних запитань. Пропонуються лише реальні цілі
  (ADR 0009): class library і тест-проєкти пропускаються (`ProjectInfo.IsLibrary`), `appsettings.local.json` —
  лише якщо поруч є будь-який `appsettings*.json` (без урахування регістру). Режим `gitignoreLocalFiles` (ADR 0009) дописує
  файл цілі в `.gitignore` проєкту під час bind/pull (`ILocalTarget.EnsureGitIgnoredAsync`). Якщо сервіс не визначається —
  `pull/use --service <name>`.
- **Key Vault — без хардкоду**:
  - читання: vault береться з URI в самому KV reference (`{"uri":"https://xxx.vault.azure.net/secrets/name"}`);
  - новий секрет: (1) вибір, збережений для проєкту/середовища → (2) найчастіший vault серед референсів сервісу →
    (3) серед `global:` референсів → (4) список з Resource Graph, ранжований (та сама RG, суфікс середовища), вибір
    користувача → (5) у майбутньому — створення vault.

### Конфігурація проєкту (ADR 0012)
- **Маніфест проєкту** (`azcfg.json`, git-tracked): сервіс, середовища, джерела конфігурації (`ConfigSource`),
  посилання на секрети (`SecretRef`), опційні `Keys`/`Ignore`, зарезервовані секції `Ci` і `Deployments`.
  **Ніколи не містить значень** (ні секретних, ні звичайних) — лише шляхи/імена; строга валідація відхиляє поля-значення.
  Порядок визначення проєкту: маніфест → `ProjectBinding` (user state) → автовизначення.
- **Профіль конвенцій** (`ConventionProfile`, для користувача — *Global Configuration Profile*): бажані імена ключів,
  розкладка файлів, режим середовищ, сховища секретів по tier-ах, провайдери. Вбудований `azcfg-default` = поточні
  конвенції (ADR 0004/0006/0008/0009). Зберігається в `profiles/` стану користувача або береться з файлу.
  Не плутати з `UserProfile` (`profile.json`).
- **Tier-и секретів** (`SecretTier`): `Local` — .NET User Secrets; `Ci` — GitHub Secrets / OIDC (OIDC бажаніше);
  `Deployed` — Azure Key Vault + Managed Identity. Нині `SecretsTarget` = `Local`, KV-референси в App Config = `Deployed`.
- **Discovery** (`DiscoveryReport`): `appsettings*.json`, `local.settings.json`, user-secrets, `launchSettings.json`,
  `.env*`, `docker-compose*` + Azure через наявні `IResourceDiscovery`/`ListServicePrefixesAsync`. Повертає лише
  шляхи ключів, **ніколи значення**. Змінні середовища машини та `.github/workflows` на цьому етапі не скануються.
- **`azcfg adopt`**: discovery → `AdoptPlan` → превʼю → підтвердження → запис `azcfg.json` + binding.
  Існуючі файли проєкту не змінюються.
- **`azcfg standardize [--profile] [-S]`** (відкладено, поправка ADR 0012): фактичний стан vs профіль → `MigrationPlan` (превʼю завжди) → підтвердження →
  застосування. Лише локальні файли/маніфест/стан користувача, remote не чіпає; за замовчуванням копіює, не видаляє.
- **Життєвий цикл**: `ValidateAsync` (`ValidationReport`) і `CompareAsync` (`DriftReport`: бажане vs фактичне) —
  `azcfg status --check` (замість окремого `validate`, поправка ADR 0012); `pull`/`push` на цьому етапі без змін, пізніше їх зв'яже з маніфестом `ProjectService`.
- **CI/CD**: у моделі (`CiConfig`, `DeploymentTarget`) лише як дані; GitHub Actions і автоматизація деплою — поза MVP.
- Core віддає дані, рішення приймає клієнт (ADR 0003); UI споживає ті самі use cases (`ProjectConfigService`).

### Стан користувача (не в репо)
`%APPDATA%/azcfg/` (Linux/macOS: `~/.config/azcfg/`):
- `profile.json` — режим середовищ, `DefaultLabel`, `GitIgnoreLocalFiles`, середовища (endpoint, label, `IsProtected`); редагується
  `azcfg config list|get|set`;
- `projects.json` — repo path → сервіс, цілі, обраний vault, активне середовище, шаблони ігнору;
- `profiles/` — профілі конвенцій `<name>.json` (ADR 0012; активний — `UserProfile.ConventionProfile`);
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
- Push з локальних файлів за замовчуванням — **upsert** (`PushSource.LocalFiles(Delete: false)`): лише Add/Update;
  локально видалені ключі дають `SyncWarning(DeleteSkipped)`. `--delete` (`plan`/`push`) додає Delete (ADR 0008).
- Локальний ігнор (`ProjectBinding.Ignore`, `azcfg ignore add|remove|list`): шаблон з `*` і необов'язковим
  `global:`; ігнорований ключ — власність локального файлу: pull його не перетирає, plan/push не бачать
  (`edit`/`promote` ігнор не застосовують).
- Попередження — дані: `SyncWarning(Kind, Key, Message)` у `PullResult.Warnings` і `SyncPlan.Warnings`.
- Запис в App Config з `If-Match: etag`; Core ставить label у кожен `Change`, Infrastructure пише рівно `entry.Label`.
- prod: apply вимагає ввести назву середовища.
- Секрети ніколи не пишуться у файли, що трекаються git (перевірка `.gitignore`); знімки (`snapshots/`)
  теж не містять значень секретів — лише fingerprint (`SecretString`, `ISecretFingerprinter`, ADR 0007).
- Auth: тільки `DefaultAzureCredential`. RBAC: читання — App Configuration Data Reader + Key Vault Secrets User;
  запис — App Configuration Data Owner + Key Vault Secrets Officer; `init` — Reader на підписках (Resource Graph).

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
    Model/                ConfigEntry, ConfigValue, ConfigEnvironment, ConfigScope, EnvironmentMode, UserProfile,
                          EnvironmentCandidate, AzureResource, ProjectInfo, LocalTargetKind, LocalFileFormat,
                          LocalTargetBinding, LocalValue, ProjectBinding, SecretString
    Mapping/              KeyMapper, JsonProjection (@kv, @global), ScopeMerger, LabelSelector
    Environments/         SeparateStoresResolver, LabelResolver, ProfileEnvironmentResolver
    Sync/                 PushSource, StateValue, StateItem, ConfigState, DiffKind, DiffItem, ConfigDiff,
                          ThreeWayDiff, IgnoreRules, SyncPlan, Change, Conflict, PullResult, SyncWarning
    Security/             HmacSecretFingerprinter
    Manifest/             ProjectManifest, ManifestEnvironment, ConfigSource, SecretRef, SecretTier, KeySpec,
                          CiConfig, DeploymentTarget, ManifestValidator (ADR 0012)
    Profiles/             ConventionProfile, BuiltInProfiles (azcfg-default)
    Discovery/            DiscoveryReport, DiscoveredSource, DiscoveryFinding
    Standardize/          AdoptPlan, MigrationPlan, MigrationStep, StandardizePlanner, DriftReport, DriftComparer
    Abstractions/         IConfigStore, ISecretStore, IResourceDiscovery, ILocalTarget, IProjectDetector,
                          IEnvironmentResolver, IUserStateStore, ISecretFingerprinter,
                          IProjectManifestStore, IConfigSourceScanner, IConventionProfileStore, IMigrationExecutor
    UseCases/             SetupService, ProjectService, SyncService, UserSettingsService, LocalTargetOption,
                          ProjectConfigService (discover/adopt/standardize/validate/compare)
  AzCfg.Infrastructure/
    Azure/                AppConfigStore, KeyVaultSecretStore, ResourceGraphDiscovery
    Local/                JsonFileTarget, UserSecretsTarget, AppSettingsLocalTarget, CsprojDetector
                          (LocalSettingsTarget — пізніше)
    State/                JsonUserStateStore, JsonConventionProfileStore
    Project/              FileProjectManifestStore, FileConfigSourceScanner, FileMigrationExecutor
    DependencyInjection.cs   services.AddAzCfg()
  AzCfg.Cli/              Spectre.Console.Cli — тільки взаємодія з користувачем; дерево команд — CliApp.Configure
  AzCfg.DevConsole/       debug-harness: REPL, що виконує команди CLI in-process (F5 + breakpoints)
tests/
  AzCfg.Core.Tests/       основна маса тестів (маппінг, merge, diff); fakes портів — Fakes/
  AzCfg.Infrastructure.Tests/
  AzCfg.Cli.Tests/        тести команд + Scenario/ — end-to-end сценарії CLI на in-memory фейках
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

public sealed record ConfigEntry(string Key, ConfigScope Scope, ConfigValue Value, string? ETag = null, string? Label = null);
public sealed record ConfigEnvironment(string Name, Uri StoreEndpoint, string? Label, bool IsProtected, string? WriteLabel = null);
public enum EnvironmentMode { StorePerEnvironment, LabelPerEnvironment }
public sealed record UserProfile(IReadOnlyList<ConfigEnvironment> Environments,
    EnvironmentMode Mode = EnvironmentMode.StorePerEnvironment, string? DefaultLabel = null,
    bool GitIgnoreLocalFiles = false);

// decision 1: локальні цілі — розробник обирає, Core лише пропонує варіанти
public enum LocalTargetKind { UserSecrets, AppSettingsLocal, LocalSettings }
public enum LocalFileFormat { NestedJson, FlatDictionary }
public sealed record LocalTargetBinding(LocalTargetKind Kind, string ProjectPath, LocalFileFormat Format);
// LocalSettings вимагає FlatDictionary — валідується в конструкторі.

// decision 2: прив'язка памʼятає вибір значень/секретів окремо (можуть збігатися)
public sealed record ProjectBinding(
    string RepoPath, string Service,
    LocalTargetBinding ValuesTarget, LocalTargetBinding SecretsTarget,
    IReadOnlyDictionary<string, Uri> VaultByEnvironment,
    string? ActiveEnvironment = null,          // середовище, що зараз у локальних файлах
    IReadOnlyList<string>? Ignore = null);     // шаблони локального ігнору (ADR 0008)

// decision 4: пара ключ/значення локального файлу; Value не потрапляє у ToString (може бути секрет)
public sealed record LocalValue(string Key, string Value, ConfigScope Scope = ConfigScope.Service); // Scope — з //@global

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
    Task<IReadOnlyList<string>> ListLabelsAsync(Uri storeEndpoint, CancellationToken ct);
}
public interface IProjectDetector
{
    Task<IReadOnlyList<ProjectInfo>> DetectAsync(string repoPath, CancellationToken ct);
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
    Task EnsureGitIgnoredAsync(LocalTargetBinding target, CancellationToken ct); // ADR 0009, лише файли в репо
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

Порти проєктної конфігурації (ADR 0012; скорочено):
```csharp
public interface IProjectManifestStore   // azcfg.json; атомарний запис
{
    Task<ProjectManifest?> LoadAsync(string repoPath, CancellationToken ct);
    Task SaveAsync(string repoPath, ProjectManifest manifest, CancellationToken ct);
}
public interface IConfigSourceScanner    // лише шляхи ключів, ніколи значення
{
    Task<IReadOnlyList<DiscoveredSource>> ScanAsync(string repoPath, CancellationToken ct);
}
public interface IConventionProfileStore
{
    Task<ConventionProfile?> LoadAsync(string nameOrPath, CancellationToken ct);
    Task SaveAsync(ConventionProfile profile, CancellationToken ct);
}
public interface IMigrationExecutor      // файлові кроки MigrationPlan через ILocalTarget; значення лише в памʼяті
{
    Task ApplyAsync(string repoPath, IReadOnlyList<MigrationStep> steps, CancellationToken ct);
}
```

### Use cases: «пропозиція → рішення»
```csharp
ProjectResolution r = await projects.ResolveAsync(repoPath, ct);
// r.Existing != null → клієнт використовує запамʼятований ProjectBinding без запитань;
// інакше клієнт обирає з r.TargetOptions (LocalTargetOption: Target, Exists, IsSafeForSecrets).
ProjectBinding b = await projects.BindAsync(repoPath, service, valuesTarget, secretsTarget, ct);

PullResult pulled = await sync.PullAsync(b, env, progress, force: false, ct); // Blocked, Warnings, Ignored

// push: бібліотечний дефолт — з локальних файлів (upsert; Delete: true додає видалення);
// edit — push відредагованого JSON; promote — лише Add ключів, яких нема в цільовому середовищі.
SyncPlan plan = await sync.PlanPushAsync(b, env, new PushSource.LocalFiles(Delete: false), ct);
SyncPlan editPlan = await sync.PlanPushAsync(b, env, new PushSource.EditedJson(json), ct);
SyncPlan promotePlan = await sync.PlanPushAsync(b, stage, new PushSource.Promote(json, "dev"), ct);

// дані для майбутнього UI (порівняння local/remote/base), окремо від plan/apply:
ConfigDiff diff = await sync.DiffAsync(b, env, ct);

ApplyResult res = await sync.ApplyAsync(plan, new ApplyOptions(ConfirmedEnvironment: "prod"), ct);
```
```csharp
// ADR 0012: усе — дані; клієнт (CLI/UI) питає й підтверджує
DiscoveryReport report = await config.DiscoverAsync(repoPath, ct);
AdoptPlan adopt = await config.PlanAdoptAsync(repoPath, ct);          // adopt.Questions — неоднозначності для клієнта
await config.ApplyAdoptAsync(repoPath, adopt, ct);                    // лише azcfg.json + binding
MigrationPlan mig = await config.PlanStandardizeAsync(repoPath, profile, ct);
await config.ApplyStandardizeAsync(repoPath, mig, selectedStepIds, ct); // лише локально, без видалень
ValidationReport v = await config.ValidateAsync(repoPath, ct);
DriftReport drift = await config.CompareAsync(repoPath, profile, ct);
```
- Прогрес: `IProgress<SyncProgress>`; логування: `ILogger<T>`.
- `SyncPlan` — record: CLI малює таблицю, `--json` серіалізує як є, UI показує diff.

### Пакети
`Azure.Identity`, `Azure.Data.AppConfiguration`, `Azure.Security.KeyVault.Secrets`,
`Azure.ResourceManager.ResourceGraph`, `Spectre.Console.Cli`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`.

## CLI
```
azcfg init [--mode stores|labels] [--store <endpoint>] [--label <default>]   майстер середовищ
azcfg config list | get <key> | set <key> <value>   mode, defaultLabel, gitignoreLocalFiles,
                                             env.<name>.label|endpoint|protected
azcfg status [--check]                       активне середовище, ⇡/⇣/конфлікти; --check — здоров'я (0.5.0)  [ADR 0013]
azcfg diff [<a>] [<b>] [--missing --all]     local ↔ env, env ↔ env, ключі, відсутні в частині середовищ  [ADR 0013]
azcfg pull [--force] [--service <name>] [--key <p>]   App Config + KV → локальні файли (--key — лише вибрані)
azcfg use <env> [--force] [--service <name>] pull + зробити середовище активним
azcfg ignore add|remove <pattern> | list     локальний ігнор ключів
azcfg plan [--delete] / push [--delete]      локальні файли → App Config (за замовчуванням upsert);
           [--key <p>] [--force]          --key — лише вибрані; push --key K --force — конфлікт на користь local
azcfg edit [--set K=V] [--unset K]           JSON у $EDITOR (або патч без редактора) → plan → підтвердження → push
azcfg promote <from> <to> [--in <file>]      нові ключі <from> → <to> через редактор
azcfg adopt                                  discovery → превʼю → azcfg.json + binding (файли проєкту не чіпає)  [ADR 0012]
(відкладено: azcfg standardize; validate — частина status --check, поправка ADR 0012)
Глобальні: --env, --json, --yes, --project <path>
```
Бюджет команд (ADR 0013): нові команди — лише `status`, `diff`, пізніше `adopt`; решта — прапорці.
`--json` — з першого дня (база для VS Code extension / UI). Користувацька довідка — `README.md`.

## Порядок робіт
MVP CLI завершено — задачі й рішення в `docs/mvp-plan.md`; режими середовищ, ігнор і режими push — ADR 0008 (0.2.0);
UX: label середовища, реальні цілі, `//@global`, відформатований JSON, `.gitignore` — ADR 0009 (0.3.0).
Далі CLI іде в `master` **раніше за UI** через гілки `release/<версія>` від `master` (див. `docs/mvp-plan.md`):
0.4.0 — щоденний workflow (`status`, `diff`, `--key`, `edit --set`, безпечний `use`) — ADR 0013, етап 8;
0.5.0 — здоров'я конфігурації (`status --check`) — етап 9; 0.6.0 — конфігурація проєкту (маніфест, discovery,
`adopt`) — ADR 0012 зі скороченням, етап 10. Після кожного релізу `master` зливається в `dev`, і UI споживає ті самі
use cases Core. Аналіз і обґрунтування — `docs/cli-roadmap.md`. План UI — `docs/ui-mvp-plan.md`.

## Майбутнє
- `promote --key/--changed` (оновлення значень, що відрізняються, а не лише додавання).
- `LocalSettingsTarget` (`local.settings.json`), git remote як сигнал зіставлення сервісу.
- Ранжування vault-ів з Resource Graph (ADR 0005, крок 4) і створення Key Vault з інструменту.
- Проєктна конфігурація далі (поза етапом 10): профілі конвенцій і `standardize`, `pull`/`push` через маніфест (sources → targets),
  віддалена частина стандартизації, дискавері `.github/workflows` (потребує YAML), модель CI/CD → генерація
  GitHub Actions environments і автоматизація деплою (OIDC, Managed Identity).
- UI: VS Code extension через `azcfg --json` або локальний `azcfg serve`; можливий десктоп-хост над тим самим Core.
