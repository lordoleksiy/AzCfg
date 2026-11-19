# MVP — план виконання

Список маленьких задач для агентів. Одна задача = одна гілка = один PR у `dev` (етапи 1–7).
**Етапи 8–10 — інакше:** гілки від `release/<версія>` (яка відгалужена від `master`), PR у `release/<версія>`,
потім один PR `release/<версія>` → `master` (див. «Гілки для етапів 8–10»).

> **Статус:** етапи 1–7 виконано. Історія версій CLI: 0.1.0 — етапи 1–6 (MVP); 0.2.0 — ADR 0008 (режими
> середовищ store / label, читання всіх labels у store-режимі, локальний ігнор `azcfg ignore`, push за
> замовчуванням — upsert, `--delete`, `azcfg config`, `pull/use --service`); 0.3.0 — етап 7, ADR 0009;
> 0.3.1 — виявлення `appsettings*.json`; 0.4.0 — етап 8 (ADR 0013). Далі: 0.5.0 — етап 9 (+ налаштування проєкту,
> ADR 0014), 0.6.0 — етап 10 (ADR 0012 скорочено + провайдери ADR 0014), етап 11 — App Service / Function App settings.
> Де текст задачі етапів 1–6 розходиться з ADR 0008/0009 — правильний ADR. Аналіз і обґрунтування порядку —
> `docs/cli-roadmap.md`.

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
  одна — один запис), крім ігнорованих ключів (ADR 0008). Щоб не втратити локальні зміни, pull відмовляє, якщо є
  незапушені зміни (див. E1), поки не передано `force`.
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

- [x] **I1. `JsonUserStateStore`** (`Infrastructure/State`)
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

- [x] **I3. `UserSecretsTarget`** (`Infrastructure/Local`, через `/add-local-target`)
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

- [x] **I5. `AppConfigStore` — читання**
  - `ReadAsync`: ключі `global:*` і `<service>:*` → `ConfigEntry` через `KeyMapper.TryParseStoreKey`.
    Content type `application/vnd.microsoft.appconfig.keyvaultref+json` → `ConfigValue.KeyVaultRef(uri)`.
  - `ListServicePrefixesAsync`: унікальні частини ключів до першої `:` (без `global`).
  - Мапінг `ConfigurationSetting → ConfigEntry` — окремий internal static метод, тестується без мережі.
  - Тести: plain, KV-ref, чужий ключ пропускається, ETag переноситься.
  - Залежить від: —

- [x] **I6. `KeyVaultSecretStore`**
  - `GetAsync(secretUri)` — vault і назва з URI; `SetAsync(vaultUri, name, value)` → URI нової версії.
  - Один `SecretClient` на vault (кеш у словнику). Auth — лише `DefaultAzureCredential`.
  - Тести: розбір URI секрету (vault, name, version).
  - Залежить від: —

- [x] **I7. `ResourceGraphDiscovery`**
  - Один KQL-запит на тип: `microsoft.appconfiguration/configurationstores`, `microsoft.keyvault/vaults`
    → `AzureResource` (з тегами й resource group).
  - Тести: мапінг рядка відповіді → `AzureResource`.
  - Залежить від: —

- [x] **I8. `AppConfigStore` — запис**
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

- [x] **L1. Каркас CLI**
  - `Program.cs`: Spectre `CommandApp` + DI (`AddAzCfg`) через `ITypeRegistrar`.
  - Глобальні опції: `--env`, `--json`, `--yes`, `--project` (за замовчуванням — поточна папка).
  - Помилки auth/RBAC (`AuthenticationFailedException`, 401/403) → зрозуміле повідомлення з потрібною роллю.
  - `--json` пише result-record через `System.Text.Json`.
  - Залежить від: —

- [x] **L2. `azcfg init`** (через `/add-cli-command`)
  - Discovery → користувач обирає/підтверджує середовища та `IsProtected` → `SaveProfileAsync`.
  - Fallback: ручне введення endpoint. `--json`: виводить кандидатів, без інтерактиву.
  - Залежить від: C1, L1, I1, I7

- [x] **L3. `azcfg pull`** (через `/add-cli-command`) — **перша корисна версія**
  - `ResolveAsync` → якщо треба, вибір сервісу й цілей → `BindAsync` → `PullAsync` → таблиця результату.
  - Залежить від: C3, L1, I1, I2, I3, I5, I6

## Етап 4. Diff, push, edit

- [x] **D1. `ThreeWayDiff.Compute`** (Core/Sync, static)
  - Вхід: base, local, remote (`ConfigState`) → `ConfigDiff` з `DiffKind` для кожного ключа.
  - Тут же нормалізація (ADR 0007): local-ключ — секрет лише якщо base/remote знає його як KV-ref;
    scope local-ключа — з remote/base, інакше `Service`.
  - Тести: кожен `DiffKind`, конфлікт (обидві сторони змінили по-різному), однакова зміна з обох боків = `Unchanged`.
  - Залежить від: M2

- [x] **D2. `ThreeWayDiff.Plan`**
  - `ConfigDiff` → `SyncPlan`: локальні зміни → `Change`; `Conflict` → `Conflicts`.
  - Правило global: зміна ключа з global → новий ключ у scope сервісу; видалення ключа з global → нічого.
    `TouchesGlobal = true` лише для змін із `@global`.
  - Тести: override замість зміни global, видалення global ігнорується, remote-зміни не потрапляють у plan.
  - Залежить від: D1

- [x] **D3. Edit JSON: export** (`JsonProjection.ToEditJson`)
  - Merged entries → JSON: вкладеність за `:`, KV-ref → `{ "@kv": "<secret-name>" }`, global-ключі — у `@global`.
  - Тести: приклад з `docs/architecture.md` (розділ «JSON-формат редагування»).
  - Залежить від: —

- [x] **D4. Edit JSON: parse** (`JsonProjection.FromEditJson`)
  - JSON → `ConfigState` + список нових секретів (`{"@kv","value"}` → `SecretString`).
    `"value": null` → помилка з назвою ключа («впиши значення або прибери ключ»). Не-рядкові скаляри → рядок.
  - Тести: round-trip з D3, `@global`, новий секрет, null-значення, невалідний JSON.
  - Залежить від: D3

- [x] **D5. `SyncService.DiffAsync` + `PlanPushAsync`**
  - Local з `PushSource.LocalFiles` (цілі) або `EditedJson` (D4); base — знімок; remote — стор → D1 → D2.
  - Vault для нових секретів (ADR 0005): збережений у binding → найчастіший серед референсів сервісу →
    серед global → інакше `RequiresVaultChoice = true`.
  - Тести на fakes: обидва джерела, кожен крок вибору vault.
  - Залежить від: D2, D4, C3

- [x] **D6. `SyncService.ApplyAsync`**
  - `IsProtected` середовище → `options.ConfirmedEnvironment` має дорівнювати назві, інакше виняток.
    `TouchesGlobal` → потрібне `ConfirmGlobal`.
  - Порядок: спершу `CreateSecret` у KV, потім App Config (референси з новими URI). Після успіху — оновити знімок.
  - Тести на fakes: відмова без підтвердження, порядок операцій, частковий збій (412) у `ApplyResult.Failed`.
  - Залежить від: D5, I8

- [x] **L4. `azcfg plan` / `azcfg push`**
  - `plan` — таблиця змін і конфліктів; `push` — plan → підтвердження (`--yes` пропускає, крім захищеного
    середовища) → apply. Є конфлікти → push відмовляє.
  - Залежить від: D6, L3

- [x] **L5. `azcfg edit`**
  - `ExportJsonAsync` → тимчасовий файл → `$EDITOR` (Windows fallback — `notepad`) → `PlanPushAsync(EditedJson)`
    → підтвердження → apply. Тимчасовий файл видаляється завжди (`finally`).
  - Залежить від: L4, D3

## Етап 5. Перемикання середовищ і promote

- [x] **E1. Захист pull від втрати змін**
  - `PullAsync` спершу рахує diff local × знімок активного середовища. Є локальні зміни і немає `force`
    → нічого не пише, `PullResult.Blocked = true` + список ключів.
  - Тести: блокування, `force` перезаписує, перший pull (немає знімка) не блокується.
  - Залежить від: D1, C3

- [x] **E2. `azcfg use <env>` + активне середовище за замовчуванням**
  - `use` = pull вказаного середовища (з E1, `--force`) і збереження `ActiveEnvironment`.
  - Усі команди без `--env` беруть `ActiveEnvironment`; якщо його немає — `dev`.
  - Залежить від: E1, L3

- [x] **E3. Promote: export** (`SyncService.ExportPromoteJsonAsync(binding, from, to, ct)`)
  - Ключі scope сервісу, що є в `from` і відсутні в `to` → edit JSON (формат D3). Plain — зі значенням з `from`;
    секрети → `{ "@kv": "<name>", "value": null }`. Global не включається.
  - Тести на fakes: лише нові ключі, наявні в `to` пропускаються, секрет без значення.
  - Залежить від: D3, C3

- [x] **E4. Promote: plan**
  - `PlanPushAsync` з `PushSource.Promote`: лише `Add` для ключів, яких нема в `to`; ключ уже з'явився в `to`
    → конфлікт; жодних `Update`/`Delete`. `null`-значення секрету → помилка (D4).
  - Тести: відсутність видалень, конфлікт при появі ключа, новий секрет створюється в vault `to`.
  - Залежить від: E3, D5

- [x] **L6. `azcfg promote <from> <to>`**
  - Export (E3) → `$EDITOR` → plan (E4) → підтвердження → apply у `<to>`. Порожній список → «нема нових ключів».
  - `--json`: без редактора — виводить export JSON; `--in <file>` — plan/apply з готового файлу.
  - Залежить від: E4, L5

## Етап 6. Випуск

- [x] **R1. Пакування і README**
  - `dotnet pack` як dotnet tool `azcfg`; README: встановлення, `az login`, ролі RBAC
    (App Configuration Data Owner, Key Vault Secrets User/Officer), приклади `init`/`pull`/`use`/`edit`/`promote`.
  - Залежить від: L6

## Етап 7. Покращення UX

- [x] **U1. Вибір label середовища**
  - У label-режимі (ADR 0008) користувач може задати label для кожного середовища; за замовчуванням label =
    назва середовища, яку він ввів перед цим.
  - Тести: дефолтний label = назва середовища, явно заданий label зберігається в профілі.
  - Залежить від: —

- [x] **U2. Пропонувати лише реальні локальні цілі**
  - Не пропонувати `AppSettingsLocal`/`UserSecrets` для кожного `.csproj` наосліп: перевіряти, чи поруч є
    `appsettings.local.json` або `appsettings.json`, і пропускати class library (без `OutputType=Exe`,
    не Web/Worker SDK, не Azure Functions).
  - Тести: temp-папка з web-проєктом, class library і проєктом без appsettings.
  - Залежить від: —

- [x] **U3. Позначка глобальних ключів у локальних файлах**
  - Позначати global-ключі в локальних цілях, щоб push знав їх scope: коментар `//@global` навпроти ключа
    або список global-ключів у конфігу binding — обрати через ADR.
  - Тести: pull пише позначку, push з позначкою відносить ключ до `global`.
  - Залежить від: —

- [x] **U4. Відформатований JSON у локальних цілях**
  - Запис у user-secrets і `appsettings*.json` — з відступами (indented), а не в один рядок.
  - Тести: записаний файл відформатований.
  - Залежить від: —

- [x] **U5. Режим збереження конфігів у проєкті + `.gitignore`**
  - Налаштування: зберігати конфіги у файлах проєкту і автоматично додавати їх у `.gitignore`.
    За замовчуванням вимкнено.
  - Тести: увімкнений режим додає файл у `.gitignore` (без дублів), вимкнений — нічого не змінює.
  - Залежить від: —

## Гілки для етапів 8–10 (CLI іде в `master` раніше за UI)
`dev` містить незавершений UI (34 коміти поверх `master`), тож через `dev` CLI-реліз не випустити. Будь-який push у
`master`, що змінює `src/**`, публікує NuGet — тому проміжні PR у `master` заборонені.
1. `release/0.4.0` від `origin/master` (етап 9 — `release/0.5.0`, етап 10 — `release/0.6.0`, кожна від `master` після
   попереднього релізу).
2. Задача = гілка `feature/<id>-<slug>` від `release/<версія>` → PR у `release/<версія>`. `/feature` за замовчуванням
   працює від `dev`: у брифі агента явно вказати базу `origin/release/<версія>` (як у брифі 0.3 `ui-mvp-plan.md`).
3. Остання задача етапу (реліз) — `<Version>`, `CHANGELOG.md`, README → один PR `release/<версія>` → `master`.
4. Одразу після злиття — `master` → `dev` (задача UI M1 у `docs/ui-mvp-plan.md`); конфлікти розв'язуються на боці `dev`.
   Після 0.5.0 і 0.6.0 — так само (M2, M3).
5. Документи етапу (ADR 0013, цей файл, `cli-roadmap.md`, `architecture.md`) потрапляють у `release/0.4.0` першим
   комітом (R0), щоб `master` після релізу описував сам себе.
6. UI не чекає на 0.5.0/0.6.0 — вони блокують лише потрапляння UI в `master`. Порядок на `dev`: M1 → U2–U4 паралельно
   з етапом 9; U1 (стан середовищ FR-E6, налаштування проєкту) — після M2, щоб брати `HealthReport` і
   `IProjectSettingsService` з Core, а не дублювати логіку в `AzCfg.Ui.Application`. Етап 10 іде паралельно з UI;
   PR0 не змінює сигнатури `ISyncService`/портів (ADR 0014), тож UI на `dev` компілюється без змін.

## Етап 8. Щоденний workflow CLI (ADR 0013) — CLI 0.4.0, у `master` до UI

Бюджет команд: нові лише `status` і `diff`; решта — прапорці існуючих команд. Core — адитивно і TDD. Значення секретів
ніколи не потрапляють у `status`/`diff`/`--json`.

- [x] **R0. Гілка `release/0.4.0` + документи** (оркестратор)
  - `release/0.4.0` від `origin/master`; коміт документів (ADR 0012 з поправкою, ADR 0013, `cli-roadmap.md`, цей файл,
    `architecture.md`, `CHANGELOG.md`, `CLAUDE.md`); push лише з OK користувача.
  - Залежить від: —

- [x] **CW1. Перенести C1/C2 у `release/0.4.0`**
  - `git cherry-pick e2b3983 3ef4bfe` (Core-хелпери: `EditJsonPatch`, `PlanSelection`, `EnvironmentComparer`,
    `KeyMapper.SecretNameOf`; use cases: `PullSelectedAsync`, `ResolveConflictAsync`, `ReadRemoteConfigAsync`,
    `EditedJson.BaseJson`, `ApplyGuard`). Зміни `docs/ui-mvp-plan.md` у цих комітах — відкинути. Поведінка CLI не змінюється.
  - Перевірка: build + test зелені; тести C1/C2 перенесені як є.
  - Залежить від: R0

- [x] **CW2. Core: `EnvironmentRules`, `KeySelector`, `UnmanagedLocalKeysAsync`**
  - `Core/Environments/EnvironmentRules` (`IsActive`, `SelectDefault`, `KindOf`) — та сама семантика, що в
    `AzCfg.Ui.Application` на `dev` (код звідти не копіюється автоматично — переписати за тестами).
  - `Core/Sync/KeySelector` — синтаксис шаблонів `IgnoreRules` (`*`, `global:`), кілька шаблонів = OR.
  - `ISyncService.UnmanagedLocalKeysAsync` (G20): ключі локальних цілей, коли `ActiveEnvironment` = null; інакше порожньо.
  - Тести (TDD): кожне правило; G20 для обох цілей і однієї спільної.
  - Залежить від: CW1

- [x] **CW3. Core: `StatusAsync` → `SyncSummary`**
  - Контракт — ADR 0013. З `DiffAsync`; лише ключі й лічильники; неактивне середовище → `IsActive = false`.
  - Тести: кожен `DiffKind` у правильному лічильнику, конфлікти й blocked keys, ігноровані, канарковий секрет відсутній у JSON.
  - Залежить від: CW1

- [x] **CW4. Core: порівняння і `MissingKeysReport`**
  - `IConfigComparisonService` (local ↔ env за fingerprint, env ↔ env через `EnvironmentComparer`), `CompareSide`,
    `ConfigComparison`; `MissingKeysReport` (static) по N середовищах; DI в `AddAzCfg()`.
  - Тести: секрет local ↔ env однаковий/різний без розкриття, env ↔ env за назвою секрету, Local ↔ Local → помилка,
    звіт відсутніх ключів (3 середовища).
  - Залежить від: CW1

- [x] **CW5. `azcfg status`** (через `/add-cli-command`)
  - Людський вивід + `--json`; `--env` для іншого середовища.
  - Тести: команда; сценарій `/cli-scenario-test` pull → локальна зміна → зміна в Azure → `status` показує ⇡1 ⇣1.
  - Залежить від: CW3

- [x] **CW6. `azcfg diff`** (через `/add-cli-command`)
  - `diff`, `diff <env>`, `diff <a> <b>` (`local` як сторона), `--same`, `--missing --all` (exit 1, якщо є відсутні).
  - Тести: усі форми аргументів; секрети лише як назви / same-different; exit code.
  - Залежить від: CW4

- [x] **CW7. `--key` для `pull`/`plan`/`push` + розв'язання конфлікту**
  - `pull --key` → `PullSelectedAsync` (лише активне середовище); `plan/push --key` → `PlanSelection.Select`;
    `pull --key K --force` = keep remote, `push --key K --force` = keep local (`ResolveConflictAsync`) + plan/apply.
  - Тести: вибір за шаблоном, неактивне середовище → зрозуміла помилка, конфлікт розв'язується лише для свого ключа,
    `--force` без `--key` поводиться як раніше.
  - Залежить від: CW1, CW2

- [x] **CW8. `edit --set/--unset/--secret` + `BaseJson`**
  - Патч експорту через `EditJsonPatch`, без редактора; `--global` → `@global`; значення секретів у командному рядку
    не приймаються. Обидва режими `edit` шлють `EditedJson(json, BaseJson)`.
  - Тести: кожен прапорець, `--global` з підтвердженням `TouchesGlobal`, паралельна зміна в Azure не відкочується (G14).
  - Залежить від: CW1

- [x] **CW9. Безпечний `use`**
  - Blocked → список ключів + підказки; перша активація з некерованими локальними ключами → підтвердження (`--yes`);
    захищене середовище → підтвердження запису секретів локально (`--confirm <ENV>`).
  - Тести: кожна гілка; `--json` не питає і повертає blocked/unmanaged як дані.
  - Залежить від: CW2

- [x] **CW10. Випуск 0.4.0**
  - README (status, diff, `--key`, `edit --set`, `use`), `CHANGELOG.md`, `<Version>` 0.4.0; PR `release/0.4.0` → `master`;
    після злиття — `master` → `dev` (UI M1).
  - Залежить від: CW5–CW9

## Етап 9. Здоров'я конфігурації — CLI 0.5.0, `azcfg status --check`

Замість окремих `doctor` і `validate`. Гілка `release/0.5.0` від `master` після 0.4.0.

- [ ] **H1. Core `HealthReport`**
  - Перевірки: вхід в Azure, доступ до кожного середовища (проба `ReadAsync`/`ListServicePrefixesAsync` — та сама, що в
    UI `AzureStatusService`), цілі в `.gitignore`, мертві KV-референси, ключі, відсутні в частині середовищ
    (`MissingKeysReport`). Severity `Error`/`Warning`; лише ключі й назви, без значень.
  - Мертві референси — новий метод порту без читання значення (`ISecretStore.ExistsAsync`, Infrastructure — властивості
    секрету); ADR-поправка до 0013 перед кодом.
  - Після H4: перевірка прив'язки (service / цілі / vault, що не відповідають реальності) з командою-виправленням
    `azcfg config set project.*` (ADR 0014).
  - Залежить від: етап 8 (CW10)

- [ ] **H2. `azcfg status --check`** — вивід звіту, `--json`, exit code ≠ 0 при `Error`.
  - Залежить від: H1

- [ ] **H4. Редаговані налаштування проєкту** (ADR 0014) — `IProjectSettingsService`, ключі `config project.*`
  (`service`, `values`, `secrets`, `vault.<env>`; `active`/`ignore` лише читання), зміна service скидає активне середовище
  і снапшоти; `pull`/`use --service X` при іншій прив'язці → помилка з підказкою.
  - Залежить від: —

- [ ] **H3. Випуск 0.5.0** (+ `master` → `dev` — UI M2; UI споживає `HealthReport` для FR-E6 і
  `IProjectSettingsService` для налаштувань проєкту в U1).
  - Залежить від: H2, H4

## Етап 10. Конфігурація проєкту (ADR 0012, скорочено) — CLI 0.6.0

Після етапів 8–9. Гілка `release/0.6.0` від `master`. Скорочення (поправка ADR 0012): `validate` — частина
`status --check`; профілі конвенцій (PC2) і `standardize` (PC8) відкладено. Маніфест, discovery і плани **ніколи не
містять значень** (ні секретних, ні звичайних). GitHub Actions і автоматизація деплою — поза етапом (у моделі лише дані).

- [x] **PC0. ADR 0012** — маніфест, профілі конвенцій, tier-и секретів, discovery, `adopt`, `standardize`, життєвий цикл, CI/CD-модель.
  - Залежить від: —

- [ ] **PR0. Провайдери віддаленої конфігурації** (ADR 0014) — `IRemoteConfigProvider`, `RemoteKind`,
  `RemoteCapabilities`, `RemoteLocation`, `AppConfigurationProvider(IConfigStore)`; `SyncService` працює через провайдери.
  Без змін для користувача: усі наявні тести проходять без правок.
  - Залежить від: —

- [ ] **PC1. Модель маніфесту** (`Core/Manifest`, віддалені `ConfigSourceKind` — за ADR 0014)
  - `ProjectManifest`, `ManifestEnvironment`, `ConfigSource`/`ConfigSourceKind`/`ConfigRole`, `SecretRef`, `SecretTier`,
    `KeySpec`, `CiConfig`, `CiEnvironment`, `DeploymentTarget`; sealed immutable records.
  - `ManifestSerializer` (System.Text.Json, коментарі й trailing commas дозволені) + `ManifestValidator`: строга
    схема (невідомі поля, зокрема `value`/`password`, → помилка), `SchemaVersion`, відповідність tier ↔ тип джерела,
    перехресні посилання (середовища, секрети, джерела), секрет не вказує на незігнорований файл.
  - Тести (TDD): round-trip, відхилення поля-значення, невідома версія схеми, кожне правило валідатора, CI/Deployments
    парсяться й валідуються без жодної автоматизації.
  - Залежить від: PR0 (`RemoteKind` у схемі v1)

- [ ] **PC2. Профілі конвенцій** (`Core/Profiles`, `Infrastructure/State`) — **відкладено** (поправка ADR 0012), не входить у 0.6.0
  - `ConventionProfile` (Naming, Layout, Environments, Secrets, Providers), `BuiltInProfiles.Default` (`azcfg-default` =
    поточні ADR 0004/0006/0008/0009), порт `IConventionProfileStore`, `JsonConventionProfileStore` (`profiles/<name>.json`
    або шлях до файлу; атомарний запис), `UserProfile.ConventionProfile = "azcfg-default"` (старий `profile.json` читається),
    `azcfg config set profile <name>`.
  - Тести: round-trip, `azcfg-default` відповідає чинним конвенціям, відсутнє поле в старому `profile.json`.
  - Залежить від: PC1

- [ ] **PC3. Сховище маніфесту** (`Infrastructure/Project`)
  - Порт `IProjectManifestStore`; `FileProjectManifestStore` (`azcfg.json` у корені репо, атомарний запис, відсутній файл → `null`).
  - Тести: temp-папка, round-trip, коментарі у файлі, пошкоджений файл → зрозуміла помилка.
  - Залежить від: PC1

- [ ] **PC4. Discovery: Core** (`Core/Discovery`, `ProjectConfigService.DiscoverAsync`)
  - `DiscoveryReport`, `DiscoveredSource` (лише `KeyPaths`), `DiscoveredEnvironment`, `DiscoveryFinding`; порт
    `IConfigSourceScanner`; об'єднання з `IProjectDetector`, `IResourceDiscovery`, `ListServicePrefixesAsync`;
    зіставлення сервісу — спільна логіка з `ProjectService`.
  - Тести на fakes: джерела з різних сканерів, finding `SecretLikeKeyInTrackedFile`, відсутність значень у серіалізованому JSON.
  - Залежить від: PC1

- [ ] **PC5. Discovery: Infrastructure** (`FileConfigSourceScanner`)
  - `appsettings*.json`, `local.settings.json`, user-secrets (через `UserSecretsTarget`), `launchSettings.json`
    (`environmentVariables`), `.env*`, `docker-compose*` (`environment:`); значення читаються лише в памʼяті для
    переліку шляхів і одразу відкидаються; `IsGitTracked` через `git ls-files`.
  - Не скануємо змінні середовища машини та `.github/workflows`.
  - Тести: temp-репо з кількома файлами; у результаті жодного значення (перевірка на «канарковому» секреті).
  - Залежить від: PC4

- [ ] **PC6. `azcfg adopt`** (через `/add-cli-command`)
  - `ProjectConfigService.PlanAdoptAsync` (маніфест + binding + `Questions` для неоднозначностей) / `ApplyAdoptAsync`
    (лише `azcfg.json` і стан користувача; файли проєкту не чіпає); ідемпотентність (merge із наявним маніфестом, показ diff).
  - CLI: discovery → питання → превʼю → підтвердження; `--yes`, `--json` (AdoptPlan як є).
  - Тести: Core на fakes; CLI-команда; сценарій `/cli-scenario-test` — незнайомий проєкт → `adopt` → `pull` працює.
  - Залежить від: PC3, PC5, L3

- [ ] **PC7. Валідація маніфесту в `status --check`** (без окремої команди `validate`)
  - `DriftComparer` (static): бажане (маніфест; без профілю — його відкладено) vs фактичне (discovery) → `DriftReport`
    (`Missing`/`Unexpected`/`Mismatch`); `ProjectConfigService.ValidateAsync`/`CompareAsync`; результати додаються в
    `HealthReport` (етап 9) → `status --check`, exit code ≠ 0 при помилках.
  - Тести: кожен `DriftKind`, проєкт без розбіжностей → порожній звіт.
  - Залежить від: PC6, H2

- [ ] **PC8. `azcfg standardize`** (через `/add-cli-command`) — **відкладено** (поправка ADR 0012), не входить у 0.6.0
  - `StandardizePlanner` (static, pure, TDD): actual vs профіль → `MigrationPlan` (`UpdateManifest`, `CreateFile`,
    `EnsureGitIgnored`, `CopyKeys`, `RenameKeys`, `SetEnvironmentMode`; кроки без значень, з `IsDestructive`).
    Проєкт, що вже відповідає профілю → порожній план.
  - Порт `IMigrationExecutor` + `FileMigrationExecutor` (через `ILocalTarget`; значення лише в памʼяті; копіювання з
    перевіркою повторним читанням; без видалень; секрети лише в ігноровані цілі).
  - CLI: `standardize [--profile <name|path>] [-S]`: превʼю завжди; без підтвердження застосувати неможливо; `--yes`
    пропускає запит; `--json` друкує план без застосування; `-S` — вибір кроків (див. відкрите питання ADR 0012).
  - Тести: планувальник (кожен крок), executor у temp-репо (секрет не потрапляє в tracked-файл і в plan/лог), CLI.
  - Залежить від: PC2, PC3, PC7

- [ ] **PC9. Випуск 0.6.0**
  - README: `adopt`, маніфест, перевірки маніфесту в `status --check`; `<Version>` 0.6.0; `CHANGELOG.md`;
    PR `release/0.6.0` → `master`, потім `master` → `dev` (UI M3).
  - Залежить від: PR0, PC6, PC7

## Поза MVP
Проєктна конфігурація (поза етапом 10): профілі конвенцій і `standardize` (PC2, PC8), `pull`/`push` через маніфест,
віддалена частина стандартизації, дискавері `.github/workflows`, генерація GitHub Actions environments і автоматизація
деплою. `promote --key/--changed`, `LocalSettingsTarget` (local.settings.json), git remote як сигнал для зіставлення
сервісу, ранжування vault-ів з Resource Graph, створення Key Vault. (`diff` і `doctor` → етапи 8–9: `diff`,
`status --check`.)
(`LabelResolver` реалізовано в 0.2.0 — ADR 0008.)
Етап 11 (ADR 0014): `AppServiceSettingsProvider` + порт `IAppSettingsStore`, `config set project.remote.<name>.<env>`,
`--remote <name>`, `LocalSettingsTarget`.
