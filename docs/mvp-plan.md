# MVP — план виконання

Список маленьких задач для агентів. Одна задача = одна гілка = один PR. З сесії 0 (див. «Сесії оркестратора») —
одна гілка `master`: задача = `feature/<id>-<slug>` від `origin/master` → PR у `master`; реліз = PR зі зміною `<Version>`.

> **Статус:** етапи 1–9 виконано. Історія версій CLI: 0.1.0 — етапи 1–6 (MVP); 0.2.0 — ADR 0008 (режими
> середовищ store / label, читання всіх labels у store-режимі, локальний ігнор `azcfg ignore`, push за
> замовчуванням — upsert, `--delete`, `azcfg config`, `pull/use --service`); 0.3.0 — етап 7, ADR 0009;
> 0.3.1 — виявлення `appsettings*.json`; 0.4.0 — етап 8 (ADR 0013); 0.5.0 — етап 9 (`status --check`, `config project.*`).
> Далі: 0.6.0 — етап 10 (мінімальний маніфест — ADR 0012, поправка 2; `local.settings.json`), 0.7.0 — UI (`azcfg ui`).
> Після UI: 0.8.0 — етап 11 (провайдери, App Service / Function App settings — ADR 0014), 0.9.0 — етап 12 (маніфест v2:
> середовища команди, розкладка ключів — ADR 0015). Обґрунтування — `docs/cli-roadmap.md` §9.
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

## Гілки
**Етапи 8–9 (історія):** `release/<версія>` від `master`, задачі — PR у неї, потім один PR у `master`; UI жив на `dev`.
Причина — будь-який push у `master` зі змінами `src/**` публікував NuGet.

**З сесії 0 — одна гілка `master`:**
1. H0 додав `dotnet nuget push --skip-duplicate`: push у `master` без зміни `<Version>` нічого не публікує. `AzCfg.Cli`
   не посилається на `AzCfg.Ui`, тож незавершений UI не потрапляє в пакет, поки X1 не додасть `azcfg ui`.
2. Задача = `feature/<id>-<slug>` від `origin/master` → PR у `master` (CI на PR — H0). `/feature` працює саме так.
3. Реліз = окремий PR: `<Version>` + release notes у csproj, `CHANGELOG.md`, README. Публікує його злиття.
4. `dev`, `release/*` і задачі-злиття M2/M3/R1 не потрібні; `dev` і `release/*` видаляються в сесії 0 після злиття.
5. Паралельні агенти — у власних worktree; один оркестратор зливає їхні PR по черзі, після кожного — build + test.

## Сесії оркестратора (з 2026-11-21)
Одна сесія = один цілісний шматок (реліз або набір екранів UI), 2–4 агенти у worktree, у кінці — злиття і перевірка.
Не дрібніше: оркестратор тримає контекст одного релізу, а не однієї задачі.

| # | Сесія | Задачі | Агенти | Залежить від |
|---|---|---|---|---|
| 0 ✅ | Одна гілка + M1 (виконано 2026-11-20; лишилось: злиття PR `dev` → `master` і прибирання гілок — `docs/ui-mvp-plan.md` §0.8) | цей документ і ADR; PR `release/0.5.0` → `master` (реліз 0.5.0); `master` → `dev` + Application делегує в Core (UI M1, включно з колишнім M2: `HealthReport`, `IProjectSettingsService`); PR `dev` → `master` без зміни версії; видалити `dev`, `release/*`, старі worktree; CI лише на PR у `master` | 1–2 | — |
| 1 | UI: основні екрани | U1 Workspace/Environments, U2 Browse, U3 Compare, U4 Sync — паралельно (`docs/ui-mvp-plan.md`) | 4 | 0 |
| 2 | CLI 0.6.0 | PC1–PC4 ∥ LS1, потім PC5 (етап 10) | 2 → 1 | 0 (паралельно з 1) |
| 3 | UI: доповнення + 0.7.0 | UX1 Problems, UX2 матриця + copy, UX3 `local.settings.json` + share, X1 `azcfg ui` ∥, потім Q1, Q2, R1 (`docs/ui-mvp-plan.md`) | 4 → 1 | 1, 2 |
| 4 | CLI 0.8.0 — App Service / Functions | RP0, потім RP1 → RP2 ∥ RP3 → RP4 → RP5, RP6 (етап 11) | 1 → 2 → 1 | 3 |
| 5 | CLI 0.9.0 — маніфест v2 | MV0, потім MV1 ∥ MV4 ∥ MV5 → MV2 → MV3, MV6 (етап 12) | 1 → 3 → 1 | 4 |

Сесії 4 і 5 — після 0.7.0, бо змінюють `SyncService`/`KeyMapper`, якими користується UI; кожна оновлює
`AzCfg.Ui.Application` у тому самому PR, якщо сигнатура змінюється. Порядок 4 ↔ 5 можна поміняти, якщо маніфест v2
потрібен раніше (тоді `remotes` у маніфесті додає сесія 4).

Сесії 1 і 2 не перетинаються за файлами (`src/AzCfg.Ui*` проти Core/Infrastructure/CLI); якщо сесії 2 треба змінити
сигнатуру, яку вже використовує UI, — вона ж оновлює `AzCfg.Ui.Application` у тому самому PR.

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

- [x] **H0. CI на PR і безпечна публікація** (перший коміт `release/0.5.0`, до задач H1–H4)
  - `.github/workflows/ci.yml`: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` на PR у `dev`,
    `release/**`, `master`. `publish-cli.yml`: `dotnet nuget push --skip-duplicate` (push у `master` без зміни
    `<Version>` не валить workflow). Не змінює `src/**` → NuGet не публікується.
  - Пак-тест `azcfg ui` додає UI-задача X1 (`docs/ui-mvp-plan.md`).
  - Залежить від: —

- [x] **H1. Core `HealthReport`**
  - Перевірки: вхід в Azure, доступ до кожного середовища (проба `ReadAsync`/`ListServicePrefixesAsync` — та сама, що в
    UI `AzureStatusService`), цілі в `.gitignore`, мертві KV-референси, ключі, відсутні в частині середовищ
    (`MissingKeysReport`). Severity `Error`/`Warning`; лише ключі й назви, без значень.
  - Мертві референси — новий метод порту без читання значення (`ISecretStore.ExistsAsync`, Infrastructure — властивості
    секрету); ADR-поправка до 0013 перед кодом.
  - Після H4: перевірка прив'язки (service / цілі / vault, що не відповідають реальності) з командою-виправленням
    `azcfg config set project.*` (ADR 0014).
  - Залежить від: етап 8 (CW10)

- [x] **H2. `azcfg status --check`** — вивід звіту, `--json`, exit code ≠ 0 при `Error`.
  - Залежить від: H1

- [x] **H4. Редаговані налаштування проєкту** (ADR 0014) — `IProjectSettingsService`, ключі `config project.*`
  (`service`, `values`, `secrets`, `vault.<env>`; `active`/`ignore` лише читання), зміна service скидає активне середовище
  і снапшоти; `pull`/`use --service X` при іншій прив'язці → помилка з підказкою.
  - Залежить від: —

- [x] **H3. Випуск 0.5.0** (+ `master` → `dev` — UI M2; UI споживає `HealthReport` для FR-E6 і
  `IProjectSettingsService` для налаштувань проєкту в U1).
  - Залежить від: H2, H4

## Етап 10. Команда і Functions — CLI 0.6.0 (сесія 2)

Контракт — ADR 0012, поправка 2 (мінімальний маніфест, без `adopt`) і ADR 0014, поправка (провайдери — етап 11). Нових
команд немає. Маніфест і перевірки **ніколи не містять значень** (ні секретних, ні звичайних). Два агенти паралельно:
маніфест (PC1–PC4, послідовно) і `local.settings.json` (LS1); потім реліз PC5.

- [x] **PC0. ADR 0012** (+ поправки 2026-11-20, 2026-11-21).
  - Залежить від: —

- [ ] **PC1. Модель маніфесту** (`Core/Manifest`)
  - `ProjectManifest(SchemaVersion, Service, ManifestTarget Values, ManifestTarget Secrets, Vaults?)`,
    `ManifestTarget(Target, Project)`; назви цілей — як у `config project.*` (`user-secrets`, `appsettings-local`,
    `local-settings`).
  - `ManifestSerializer` (System.Text.Json; коментарі й trailing commas дозволені) + `ManifestValidator`: строга схема
    (невідоме поле, зокрема `value`/`password`, → помилка з назвою поля), `SchemaVersion` > підтримуваної → помилка з
    підказкою оновитися, `Project` — відносний шлях без виходу за корінь репо, vault — абсолютний https URI.
  - Тести (TDD): round-trip, коментарі, кожне правило валідатора, канарковий `"value"` відхиляється.
  - Залежить від: —

- [ ] **PC2. Сховище маніфесту** (`Infrastructure/Project`)
  - Порт `IProjectManifestStore` (`FindAsync(path)` → `(ProjectManifest, string FilePath)?` — найближчий `azcfg.json`
    від `path` угору до кореня git; `SaveAsync(filePath, manifest)`); `FileProjectManifestStore` (атомарний запис як
    `JsonUserStateStore`, пошкоджений файл → зрозуміла помилка з шляхом). DI в `AddAzCfg()`.
  - Тести: temp-репо, пошук із вкладеної теки, зупинка на корені git, round-trip.
  - Залежить від: PC1

- [ ] **PC3. Маніфест у прив'язці** (Core + CLI)
  - `ProjectService.ResolveAsync`: немає binding + є маніфест → binding з маніфесту без майстра; binding з іншим сервісом →
    правило `config set project.service` (скинути активне середовище і снапшоти). `ActiveEnvironment`, `Ignore`, снапшоти
    лишаються в стані користувача.
  - `ProjectSettingsService`: є маніфест → `project.service|values|secrets|vault.<env>` пишуть і маніфест, підказка
    «shared via azcfg.json — commit it»; новий ключ `project.shared` (лише `true`: записати `azcfg.json` з binding;
    читання → `true` + шлях / `false`).
  - CLI: майстер першого `pull`/`use` пропонує «save as azcfg.json for the team» (за замовчуванням ні; `--yes`/`--json`
    ніколи не пишуть); `config list` показує `project.shared`.
  - Тести: Core на fakes (кожне правило); сценарій `/cli-scenario-test`: розробник A → `config set project.shared true`;
    розробник B (порожній стан користувача) → `pull` без майстра, ті самі цілі.
  - Залежить від: PC2

- [ ] **PC4. Перевірки в `status --check`**
  - `HealthCheckKind.ManifestInvalid` (Error): нечитабельний файл, помилка схеми, непідтримувана версія.
  - `HealthCheckKind.SecretLikeKeyInTrackedFile` (Warning): новий порт `ITrackedSettingsReader.ReadKeysAsync(repoPath)` →
    `TrackedSettingKey(File, Key, bool HasValue)` для git-tracked `appsettings*.json` (Infrastructure: `git ls-files`,
    значення лише в пам'яті для `HasValue`); Core відбирає ключі, назва яких містить
    `password|secret|token|apikey|connectionstring` (без регістру) і `HasValue`.
  - Тести: кожна перевірка; канарковий секрет відсутній у `HealthReport` JSON; поза git → перевірка пропускається.
  - Залежить від: PC1 (ManifestInvalid), H1

- [x] **LS1. `LocalSettingsTarget`** (через `/add-local-target`, ADR 0006)
  - `local.settings.json` поруч із `.csproj` Azure Functions: читання/запис лише секції `Values` (`FlatDictionary`, ключі
    `A:B` як є, без вкладення); інші секції (`IsEncrypted`, `Host`, `ConnectionStrings`) зберігаються як були; відсутній
    файл → створити `{ "IsEncrypted": false, "Values": {} }`. Безпека — як `AppSettingsLocalTarget` (git-ignored і
    не tracked).
  - `ProjectService` пропонує ціль для проєктів з `IsAzureFunctions`; `config set project.values|secrets local-settings`
    приймається.
  - `//@global` (ADR 0009) у `Values` — лише якщо Azure Functions Core Tools приймає коментарі в `local.settings.json`
    (перевірити вручну й записати в PR); інакше позначки не пишуться, scope — з remote/base, як до ADR 0009.
  - Тести: round-trip, збереження інших секцій, safety у temp git-репо.
  - Залежить від: —

- [ ] **PC5. Випуск 0.6.0**
  - README (`azcfg.json`, `project.shared`, нові перевірки, `local.settings.json`), `CHANGELOG.md`, `<Version>` 0.6.0 +
    release notes; PR у `master`.
  - Залежить від: PC3, PC4, LS1

## Етап 11. App Service / Function App settings — CLI 0.8.0 (сесія 4)

Контракт — ADR 0014 (провайдери) + поправка RP0. Обґрунтування потреби — `docs/cli-roadmap.md` §9: сервіси на App Service
тримають деплоєний конфіг в App Settings (часто без App Configuration у рантаймі), Functions — цільова аудиторія 0.6.0.
Нових команд немає: `--remote`, сторона `env@remote`, `config project.remote.*`. Значення секретів — як і раніше, ніде.

- [ ] **RP0. Поправка ADR 0014** (оркестратор, до коду)
  - Етап 11 заплановано (знімається умова «лише за потреби»); додається: сторона `env@remote` у `diff`/`promote`,
    `promote --key`, `HealthCheckKind.RemoteDrift`; явно — поправка 2 ADR 0012 перекриває «`project.*` never the manifest».
  - Залежить від: 0.7.0

- [ ] **RP1. Core: провайдери** (`Core/Remotes`, TDD)
  - `RemoteKind`, `RemoteCapabilities`, `RemoteLocation`, `IRemoteConfigProvider`; `AppConfigurationProvider(IConfigStore)` —
    поточна поведінка, винесена з `SyncService` без змін; `SyncService` працює через `IEnumerable<IRemoteConfigProvider>`;
    `RemoteResolver` (static).
  - Приймання: **усі наявні тести проходять без змін**; правила capabilities (немає `ServiceScopes` → `ApplyBlocker` для
    global, немає `Labels` → label ігнорується, немає `PerKeyConcurrency` → read-modify-write з перевіркою `Before`,
    `RestartsOnWrite` → `SyncWarningKind.RestartsApp`) — на фейковому провайдері.
  - Залежить від: RP0

- [ ] **RP2. Infrastructure: `IAppSettingsStore`** (порт у Core, реалізація через ARM)
  - `ReadAsync(resourceId, slot)` → `AppSettingsSnapshot(Settings, ETag)`, `ReplaceAsync(resourceId, slot, settings, expectedETag)`;
    Web App і Function App, слоти; 412 → `ApplyResult.Failed`; помилки доступу — через `AzureErrors`. DI в `AddAzCfg()`.
  - Залежить від: RP1

- [ ] **RP3. Core: `AppServiceSettingsProvider`**
  - `A__B` ↔ `A:B` (читання приймає обидва, запис — `__`); `@Microsoft.KeyVault(SecretUri=…)` ↔ `ConfigValue.KeyVaultRef`;
    усе `ConfigScope.Service`; `PlatformIgnore`: `WEBSITE_*`, `FUNCTIONS_*`, `AzureWebJobs*`, `APPLICATIONINSIGHTS_*`,
    `APPINSIGHTS_*`, `SCM_*`. Capabilities: `Delete`, `KeyVaultReferences`, `RestartsOnWrite`.
  - Тести: мапінг, KV-референси без читання значень, platform-ключі не потрапляють у plan, конфлікт при зміні між read і replace.
  - Залежить від: RP1 (паралельно з RP2 — на фейку порту)

- [ ] **RP4. Прив'язка і CLI `--remote`**
  - `ProjectBinding.Remotes` (`RemoteBinding(Name, Kind, LocationByEnvironment)`, опційне, старі `projects.json` без міграції);
    `config set project.remote.<name>.<env> <resource-id>[@<slot>]` (kind — з типу ресурсу); `--remote <name>` для `pull`,
    `use`, `status`, `diff`, `plan`, `push`, `edit`; знімки не-основного ремоута — `snapshots/<repo>/<env>@<remote>.json`.
  - Без `--remote` — рівно поведінка 0.7.0 (`appconfig`).
  - Тести: сценарій `/cli-scenario-test` Function App: `config set project.remote…` → `pull --remote app` у
    `local.settings.json` → зміна → `plan --remote app` (warning `RestartsApp`) → `push`.
  - Залежить від: RP2, RP3

- [ ] **RP5. Між ремоутами: `env@remote`, `promote --key`, `RemoteDrift`**
  - `diff dev@app dev`, `promote dev@app dev [--key <pattern>]` (міграція App Service → App Configuration і навпаки; KV-референси
    переносяться як референси); `promote --key` працює і без ремоутів.
  - `status --check`: `RemoteDrift` (Warning) — ключ є в основному і в іншому ремоуті одного середовища з різними значеннями
    (секрети — за назвою/референсом, не за значенням).
  - Тести: усі форми сторін; platform-ключі не мігрують; канарковий секрет відсутній у `--json`.
  - Залежить від: RP4

- [ ] **RP6. Випуск 0.8.0** — README (сценарії App Service і Functions, міграція в App Configuration), `CHANGELOG.md`,
  `<Version>`; UI: вибір ремоута — окрема задача UI-плану після релізу.
  - Залежить від: RP5

## Етап 12. Маніфест v2: середовища команди і розкладка ключів — CLI 0.9.0 (сесія 5)

Контракт — ADR 0015 (MV0). Усе адитивне: без `azcfg.json` v2 поведінка як у 0.8.0; файл v1 лишається валідним; дефолт
розкладки = ADR 0004. Нових команд немає.

- [ ] **MV0. ADR 0015 + поправки ADR 0004, 0008, 0012** (оркестратор, до коду)
  - ADR 0015: `KeyLayout`, `environments` / `remotes` / `values|secrets.file` у маніфесті v2, пріоритет рівнів
    (прапорець > особисте в `projects.json` > `azcfg.json` > `profile.json` > автовизначення).
  - Поправки: ADR 0004 (конвенція — дефолт розкладки), ADR 0008 (режим і середовища можуть приходити з маніфесту),
    ADR 0012 (endpoints/resource id у маніфесті дозволені, опційно), ADR 0006 (власна назва JSON-цілі, лише git-ignored).
  - Залежить від: 0.8.0

- [ ] **MV1. Core: `KeyLayout`** (TDD)
  - `KeyLayout(ServicePrefix, GlobalPrefix?)`, дефолт `("<service>:", "global:")`; `KeyMapper`, `ScopeMerger`, `JsonProjection`,
    `ListServicePrefixesAsync`-евристика сервісу приймають layout; `ServicePrefix = ""` — store/label належить застосунку;
    `GlobalPrefix = null` → `@global` і `//@global` дають `ApplyBlocker`. KV-назви `:` → `--` без змін.
  - Приймання: наявні тести без змін (дефолт); параметризований набір для трьох розкладок.
  - Залежить від: MV0

- [ ] **MV2. Модель і валідатор маніфесту v2**
  - `schemaVersion: 2`; опційні `layout`, `environments` (`mode`, на середовище `appconfig`, `label`, `protected`), `remotes`
    (як `ProjectBinding.Remotes`), `values|secrets.file`. Строга схема, value-free, канарковий `"value"` відхиляється;
    v1 читається як раніше; v2 у 0.8.0 → помилка з підказкою оновитися (вже поведінка v1-валідатора).
  - Залежить від: MV1

- [ ] **MV3. Розв'язання рівнів**
  - Середовища: є `environments` у маніфесті → для цього репо вони замінюють список профілю (без злиття по полях); `pull`/`use`
    без `profile.json` працюють (`init` не потрібен); `config list`, `status` показують джерело середовищ.
  - `layout`, `remotes` з маніфесту потрапляють у binding як спільні поля (правило поправки 2 ADR 0012).
  - Тести: сценарій `/cli-scenario-test` «новий розробник»: порожній стан користувача + `azcfg.json` v2 → `pull` без `init` і
    без майстра.
  - Залежить від: MV2

- [ ] **MV4. JSON-ціль із власною назвою файлу**
  - `appsettings-local` + `file` (напр. `appsettings.Development.json`) у маніфесті й `config set project.values
    appsettings-local:<project>#<file>`; ціль приймається лише якщо файл git-ignored і не tracked (і для значень).
  - Залежить від: MV0

- [ ] **MV5. `status --check`: `LocalTargetNotLoaded`** (Warning, евристика)
  - Ціль, яку застосунок, імовірно, не читає: `appsettings.local.json` / власна назва не згадується в `*.cs` проєкту;
    user-secrets без `UserSecretsId`. Лише назви файлів у звіті; без блокування.
  - Залежить від: MV0

- [ ] **MV6. Випуск 0.9.0** — README (онбординг «clone → `az login` → `pull`», розкладки ключів), `CHANGELOG.md`, `<Version>`.
  - Залежить від: MV3, MV4, MV5

## Поза MVP
Кандидати — лише з конкретним користувачем і окремим ADR:
- Прив'язка без локальних цілей (застосунок читає App Configuration у рантаймі, azcfg — лише `edit`/`diff`/`status`).
- Key Vault як самостійний ремоут (команди з `AddAzureKeyVault` без App Configuration) — потребує поправки ADR 0014.
- UI «ефективна конфігурація» (read-only шари `appsettings*.json` → ціль → env vars), Container Apps як ремоут.
- `promote --changed`, git remote як сигнал для зіставлення сервісу, ранжування vault-ів з Resource Graph, створення Key Vault.

Не плануються (`docs/cli-roadmap.md` §9):
- З ADR 0012 (поправка 2): `adopt`, discovery-сканери (`launchSettings`, `.env*`, `docker-compose*`, `.github/workflows`),
  drift-звіт, джерела/tier-и секретів/`KeySpec` у маніфесті, профілі конвенцій і `standardize`, CI/CD-модель і
  генерація GitHub Actions, `pull`/`push` за повним маніфестом.
- Менеджер env vars / `.env`, запис у tracked `appsettings*.json`, постійна синхронізація App Configuration → App Service.
(`LabelResolver` реалізовано в 0.2.0 — ADR 0008; `diff` і `status --check` — етапи 8–9.)
