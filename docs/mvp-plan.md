# MVP — план виконання

Список маленьких задач для агентів. Одна задача = одна гілка = один PR у `dev`.

## Як взяти задачу
1. Прочитай `CLAUDE.md` (правила шарів, безпека, конвенції ключів).
2. Візьми **першу невідмічену** задачу, у якої всі `Залежить від` уже відмічені `[x]`.
3. Запусти `/feature <ID> <назва задачі>` — скіл створить гілку, спершу тести, потім код, build/test, PR.
4. У тому ж PR відміть задачу тут: `- [ ]` → `- [x]`.
5. Деталі моделі й портів — `docs/architecture.md`, рішення — `docs/adr/`. Читай лише те, що потрібно задачі.

## Принципи
- Логіка живе в трьох сервісах Core (`SetupService`, `ProjectService`, `SyncService`) і статичних хелперах
  (`KeyMapper`, `ScopeMerger`, `JsonProjection`, `ThreeWayDiff`). Новий клас/порт — лише якщо без нього не можна
  (межа IO/Azure).
- Готові бібліотеки замість власного коду, якщо вони є (див. задачі). Пакети — лише через `Directory.Packages.props`.
- Core тестується на fake-реалізаціях портів, написаних вручну в `tests/AzCfg.Core.Tests/Fakes/` (без mock-бібліотек).
- Кожна задача Infrastructure сама реєструє свій тип в `AddAzCfg()`.
- Не роби нічого поза своєю задачею.

## Ключові рішення MVP
- **Pull повністю перезаписує** локальні цілі (значення → `ValuesTarget`, секрети → `SecretsTarget`; якщо ціль
  одна — один запис). Щоб не втратити локальні зміни, pull відмовляє, якщо є незапушені зміни (див. B2), поки
  не передано `force`.
- **Захищене середовище**: `ConfigEnvironment.IsProtected`. За замовчуванням визначається за назвою
  (`prod`, `production`, `prd` — без урахування регістру), користувач може змінити в `init`. Apply в захищене
  середовище вимагає ввести його назву.
- **Активне середовище проєкту**: `ProjectBinding.ActiveEnvironment` — яке середовище зараз у локальних файлах.
  Команди без `--env` працюють з ним. `azcfg use <env>` = pull цього середовища + запам'ятати його активним.
- **Promote** (`azcfg promote dev stage`): беремо ключі сервісу, які є в `dev`, але відсутні в `stage`;
  відкриваємо їх як edit JSON для `stage`; розробник правиться під stage; plan → push у `stage`.
  Promote лише **додає** ключі — ніколи не оновлює й не видаляє існуючі. Секрети не копіюються: вони приходять
  як `{ "@kv": "<name>", "value": null }`, і розробник мусить вписати значення для stage або прибрати ключ.
  `global:*` не промоутиться.

---

## Етап 1. Модель

- [x] **M1. Захищені середовища і профіль**
  - `ConfigEnvironment` + `bool IsProtected`; статичний `ConfigEnvironment.IsProtectedByName(string name)`.
  - `UserProfile(IReadOnlyList<ConfigEnvironment> Environments)` замість словника `name → Uri`.
  - `SeparateStoresResolver : IEnvironmentResolver` (Core) — повертає середовища профілю, `Label = null`.
  - Тести: `IsProtectedByName` (prod/Production/prd → true, dev/stage → false), resolver.
  - Залежить від: —

- [x] **M2. Доповнення моделі синхронізації**
  - `Conflict(Key, StateItem? Base, StateItem? Local, StateItem? Remote)` — як у `DiffItem` (без `ConfigEntry`).
  - `PullResult(string Environment, int ValuesWritten, int SecretsWritten, bool Blocked, IReadOnlyList<string> LocalChanges)`.
  - `ProjectBinding` + `string? ActiveEnvironment`.
  - `PushSource.Promote(string Json, string FromEnvironment)`.
  - Тести: серіалізація `Conflict`/`PullResult` у JSON не містить значень секретів.
  - Залежить від: —

## Етап 2. Infrastructure

- [ ] **I1. `JsonUserStateStore`** (`Infrastructure/State`)
  - Корінь: `%APPDATA%/azcfg` (Linux/macOS `~/.config/azcfg`); конструктор приймає корінь (для тестів — temp dir).
  - `profile.json`, `projects.json` (ключ — повний шлях репо), `snapshots/<repo-hash>/<env>.json`, `fingerprint.key`
    (32 випадкові байти, створюється один раз).
  - Запис атомарний: тимчасовий файл + `File.Move(overwrite: true)`.
  - Тести: round-trip кожного файлу, відсутній файл → `null`, ключ стабільний між викликами.
  - Залежить від: M1, M2

- [x] **I2. `AppSettingsLocalTarget`** (`Infrastructure/Local`)
  - Читання: `Microsoft.Extensions.Configuration.Json` (`ConfigurationBuilder().AddJsonFile(...)`) — сам дає
    плоскі ключі `A:B`. Запис: `JsonProjection.ToNestedJson`, повний перезапис файлу.
  - Файл: `appsettings.local.json` поруч із `.csproj` (`LocalTargetBinding.ProjectPath`).
  - Спільну частину read/write винеси в базовий клас `JsonFileTarget` — його перевикористає I3.
  - `InspectAsync`: `IsSafeForSecrets` = файл ігнорується (`git check-ignore -q <file>`) і не затреканий
    (`git ls-files --error-unmatch <file>` завершується з помилкою). Поза git-репо → `false`.
  - Додати пакет `Microsoft.Extensions.Configuration.Json`.
  - Тести: round-trip вкладеного JSON, відсутній файл → порожній список, safety у temp git-репо.
  - Залежить від: —

- [ ] **I3. `UserSecretsTarget`** (`Infrastructure/Local`, через `/add-local-target`)
  - Шлях: `PathHelper.GetSecretsPathFromSecretsId(id)` з `Microsoft.Extensions.Configuration.UserSecrets`.
  - Немає `UserSecretsId` у `.csproj` → виконати `dotnet user-secrets init --project <csproj>`.
  - `IsSafeForSecrets = true` завжди.
  - Додати пакет `Microsoft.Extensions.Configuration.UserSecrets`.
  - Тести: round-trip через тимчасовий `UserSecretsId`.
  - Залежить від: I2

- [x] **I4. `IProjectDetector` + `CsprojDetector`**
  - Порт у `Core/Abstractions`: `Task<IReadOnlyList<ProjectInfo>> DetectAsync(string repoPath, CancellationToken ct)`.
  - Реалізація: пошук `*.csproj` (без `bin/`, `obj/`), з XML — `UserSecretsId`, `AzureFunctionsVersion` → `IsAzureFunctions`.
  - Тести: temp-папка з кількома csproj.
  - Залежить від: —

- [ ] **I5. `AppConfigStore` — читання**
  - `ReadAsync`: ключі `global:*` і `<service>:*` → `ConfigEntry` через `KeyMapper.TryParseStoreKey`.
    Content type `application/vnd.microsoft.appconfig.keyvaultref+json` → `ConfigValue.KeyVaultRef(uri)`.
  - `ListServicePrefixesAsync`: унікальні частини ключів до першої `:` (без `global`).
  - Мапінг `ConfigurationSetting → ConfigEntry` — окремий internal static метод, тестується без мережі.
  - Тести: plain, KV-ref, чужий ключ пропускається, ETag переноситься.
  - Залежить від: —

- [ ] **I6. `KeyVaultSecretStore`**
  - `GetAsync(secretUri)` — vault і назва з URI; `SetAsync(vaultUri, name, value)` → URI нової версії.
  - Один `SecretClient` на vault (кеш у словнику). Auth — лише `DefaultAzureCredential`.
  - Тести: розбір URI секрету (vault, name, version).
  - Залежить від: —

- [ ] **I7. `ResourceGraphDiscovery`**
  - Один KQL-запит на тип: `microsoft.appconfiguration/configurationstores`, `microsoft.keyvault/vaults`
    → `AzureResource` (з тегами й resource group).
  - Тести: мапінг рядка відповіді → `AzureResource`.
  - Залежить від: —

- [ ] **I8. `AppConfigStore` — запис**
  - `ApplyAsync`: `Add`/`Update` → `SetConfigurationSetting` з `If-Match: etag` (для `Add` — `If-None-Match: *`);
    `Delete` → видалення з etag; KV-ref пишеться з правильним content type. HTTP 412 → ключ у `ApplyResult.Failed`.
  - Тести: побудова `ConfigurationSetting` з `Change` (без мережі).
  - Залежить від: I5

## Етап 3. Core use cases + перша версія CLI

- [x] **C1. `SetupService`**
  - `DiscoverEnvironmentsAsync` повертає кандидатів: середовище за тегом `environment`, інакше за суфіксом
    назви (`-dev`, `-stg`/`-stage`, `-prod`). Невизначені — у кандидатах з `Name = null`, рішення приймає клієнт.
    `IsProtected` — за `IsProtectedByName`.
  - `SaveProfileAsync` → `IUserStateStore`.
  - Тести: тег важливіший за суфікс, невідомий суфікс, кілька stores на одне середовище.
  - Залежить від: M1

- [x] **C2. `ProjectService`**
  - `ResolveAsync`: є збережений binding → `Existing`. Інакше: проєкти з `IProjectDetector`, префікси з dev store,
    зіставлення назви csproj/папки з префіксом (ordinal ignore case, `-`/`.` вважаються однаковими).
    Один точний збіг → `IsConfident = true`. `TargetOptions` — з `ILocalTarget.InspectAsync`.
  - `BindAsync`: небезпечна ціль для секретів → `InvalidOperationException`; зберігає binding.
  - Тести на fakes: existing, впевнений збіг, кілька кандидатів, відмова небезпечній цілі.
  - Залежить від: I4, M2

- [x] **C3. `SyncService.PullAsync`**
  - Читає remote (`IConfigStore`) → `ScopeMerger.Merge` → KV-ref резолвляться через `ISecretStore`.
  - Повний перезапис: значення → `ValuesTarget`, секрети → `SecretsTarget` (одна ціль → один запис).
  - Перед записом повторно перевіряє `IsSafeForSecrets` для `SecretsTarget`; небезпечно → нічого не пише, виняток.
  - Зберігає знімок (секрети — лише fingerprint), ставить `ActiveEnvironment`, повертає `PullResult`.
  - Тести на fakes: розділення значень/секретів, service перекриває global, у знімку немає значень секретів.
  - Залежить від: C2, M2

- [ ] **L1. Каркас CLI**
  - `Program.cs`: Spectre `CommandApp` + DI (`AddAzCfg`) через `ITypeRegistrar`.
  - Глобальні опції: `--env`, `--json`, `--yes`, `--project` (за замовчуванням — поточна папка).
  - Помилки auth/RBAC (`AuthenticationFailedException`, 401/403) → зрозуміле повідомлення з потрібною роллю.
  - `--json` пише result-record через `System.Text.Json`.
  - Залежить від: —

- [ ] **L2. `azcfg init`** (через `/add-cli-command`)
  - Discovery → користувач обирає/підтверджує середовища та `IsProtected` → `SaveProfileAsync`.
  - Fallback: ручне введення endpoint. `--json`: виводить кандидатів, без інтерактиву.
  - Залежить від: C1, L1, I1, I7

- [ ] **L3. `azcfg pull`** (через `/add-cli-command`) — **перша корисна версія**
  - `ResolveAsync` → якщо треба, вибір сервісу й цілей → `BindAsync` → `PullAsync` → таблиця результату.
  - Залежить від: C3, L1, I1, I2, I3, I5, I6

## Етап 4. Diff, push, edit

- [x] **D1. `ThreeWayDiff.Compute`** (Core/Sync, static)
  - Вхід: base, local, remote (`ConfigState`) → `ConfigDiff` з `DiffKind` для кожного ключа.
  - Тут же нормалізація (ADR 0007): local-ключ — секрет лише якщо base/remote знає його як KV-ref;
    scope local-ключа — з remote/base, інакше `Service`.
  - Тести: кожен `DiffKind`, конфлікт (обидві сторони змінили по-різному), однакова зміна з обох боків = `Unchanged`.
  - Залежить від: M2

- [ ] **D2. `ThreeWayDiff.Plan`**
  - `ConfigDiff` → `SyncPlan`: локальні зміни → `Change`; `Conflict` → `Conflicts`.
  - Правило global: зміна ключа з global → новий ключ у scope сервісу; видалення ключа з global → нічого.
    `TouchesGlobal = true` лише для змін із `@global`.
  - Тести: override замість зміни global, видалення global ігнорується, remote-зміни не потрапляють у plan.
  - Залежить від: D1

- [ ] **D3. Edit JSON: export** (`JsonProjection.ToEditJson`)
  - Merged entries → JSON: вкладеність за `:`, KV-ref → `{ "@kv": "<secret-name>" }`, global-ключі — у `@global`.
  - Тести: приклад з `docs/architecture.md` (розділ «JSON-формат редагування»).
  - Залежить від: —

- [ ] **D4. Edit JSON: parse** (`JsonProjection.FromEditJson`)
  - JSON → `ConfigState` + список нових секретів (`{"@kv","value"}` → `SecretString`).
    `"value": null` → помилка з назвою ключа («впиши значення або прибери ключ»). Не-рядкові скаляри → рядок.
  - Тести: round-trip з D3, `@global`, новий секрет, null-значення, невалідний JSON.
  - Залежить від: D3

- [ ] **D5. `SyncService.DiffAsync` + `PlanPushAsync`**
  - Local з `PushSource.LocalFiles` (цілі) або `EditedJson` (D4); base — знімок; remote — стор → D1 → D2.
  - Vault для нових секретів (ADR 0005): збережений у binding → найчастіший серед референсів сервісу →
    серед global → інакше `RequiresVaultChoice = true`.
  - Тести на fakes: обидва джерела, кожен крок вибору vault.
  - Залежить від: D2, D4, C3

- [ ] **D6. `SyncService.ApplyAsync`**
  - `IsProtected` середовище → `options.ConfirmedEnvironment` має дорівнювати назві, інакше виняток.
    `TouchesGlobal` → потрібне `ConfirmGlobal`.
  - Порядок: спершу `CreateSecret` у KV, потім App Config (референси з новими URI). Після успіху — оновити знімок.
  - Тести на fakes: відмова без підтвердження, порядок операцій, частковий збій (412) у `ApplyResult.Failed`.
  - Залежить від: D5, I8

- [ ] **L4. `azcfg plan` / `azcfg push`**
  - `plan` — таблиця змін і конфліктів; `push` — plan → підтвердження (`--yes` пропускає, крім захищеного
    середовища) → apply. Є конфлікти → push відмовляє.
  - Залежить від: D6, L3

- [ ] **L5. `azcfg edit`**
  - `ExportJsonAsync` → тимчасовий файл → `$EDITOR` (Windows fallback — `notepad`) → `PlanPushAsync(EditedJson)`
    → підтвердження → apply. Тимчасовий файл видаляється завжди (`finally`).
  - Залежить від: L4, D3

## Етап 5. Перемикання середовищ і promote

- [ ] **E1. Захист pull від втрати змін**
  - `PullAsync` спершу рахує diff local × знімок активного середовища. Є локальні зміни і немає `force`
    → нічого не пише, `PullResult.Blocked = true` + список ключів.
  - Тести: блокування, `force` перезаписує, перший pull (немає знімка) не блокується.
  - Залежить від: D1, C3

- [ ] **E2. `azcfg use <env>` + активне середовище за замовчуванням**
  - `use` = pull вказаного середовища (з E1, `--force`) і збереження `ActiveEnvironment`.
  - Усі команди без `--env` беруть `ActiveEnvironment`; якщо його немає — `dev`.
  - Залежить від: E1, L3

- [ ] **E3. Promote: export** (`SyncService.ExportPromoteJsonAsync(binding, from, to, ct)`)
  - Ключі scope сервісу, що є в `from` і відсутні в `to` → edit JSON (формат D3). Plain — зі значенням з `from`;
    секрети → `{ "@kv": "<name>", "value": null }`. Global не включається.
  - Тести на fakes: лише нові ключі, наявні в `to` пропускаються, секрет без значення.
  - Залежить від: D3, C3

- [ ] **E4. Promote: plan**
  - `PlanPushAsync` з `PushSource.Promote`: лише `Add` для ключів, яких нема в `to`; ключ уже з'явився в `to`
    → конфлікт; жодних `Update`/`Delete`. `null`-значення секрету → помилка (D4).
  - Тести: відсутність видалень, конфлікт при появі ключа, новий секрет створюється в vault `to`.
  - Залежить від: E3, D5

- [ ] **L6. `azcfg promote <from> <to>`**
  - Export (E3) → `$EDITOR` → plan (E4) → підтвердження → apply у `<to>`. Порожній список → «нема нових ключів».
  - `--json`: без редактора — виводить export JSON; `--in <file>` — plan/apply з готового файлу.
  - Залежить від: E4, L5

## Етап 6. Випуск

- [ ] **R1. Пакування і README**
  - `dotnet pack` як dotnet tool `azcfg`; README: встановлення, `az login`, ролі RBAC
    (App Configuration Data Owner, Key Vault Secrets User/Officer), приклади `init`/`pull`/`use`/`edit`/`promote`.
  - Залежить від: L6

## Поза MVP
`doctor`, `diff` між середовищами, `LocalSettingsTarget` (local.settings.json), `LabelResolver`,
git remote як сигнал для зіставлення сервісу, створення Key Vault.
