# 0009. Покращення UX: label середовища, реальні локальні цілі, позначка `//@global`, відформатований JSON, `.gitignore`

Status: accepted (MVP, етап 7: U1–U5)

## Контекст
- У label-режимі (ADR 0008) `init` не дає задати label середовища, доданого вручну, — label питається до назви
  і без підказки.
- `ProjectService.ResolveAsync` пропонує `AppSettingsLocal`/`UserSecrets` для кожного `.csproj`, включно з class
  library і тест-проєктами.
- Локальні цілі не зберігають scope ключа: усе, що прочитано з файлу, вважається `Service`. Змінений локально
  global-ключ при push стає service-override, а явно віднести ключ до `global` з файлу неможливо.
- JSON у user-secrets і `appsettings.local.json` пишеться в один рядок.
- Немає способу тримати конфіги (включно з секретами) у файлі проєкту, щоб він сам потрапляв у `.gitignore`.

## Рішення
### U1. Label середовища в label-режимі (лише CLI, `InitCommand`)
- Для кожного середовища: спершу назва, потім `TextPrompt` «Label for '<name>':» з `DefaultValue`:
  - label, обраний зі store, — підказка = цей label (`Development` залишається `Development`);
  - доданий вручну — підказка = **назва середовища, щойно введена** (питання «Label:» перед назвою прибрано).
- Введений label зберігається явно в `ConfigEnvironment.Label` профілю (навіть якщо дорівнює назві).
- `--yes` не змінюється.

### U2. Лише реальні локальні цілі
- `ProjectInfo(..., string? UserSecretsId, bool IsLibrary = false)`. `CsprojDetector` ставить `IsLibrary = true`, якщо
  проєкт **не** є застосунком: немає `OutputType` `Exe`/`WinExe`, SDK не `Microsoft.NET.Sdk.Web`/`Microsoft.NET.Sdk.Worker`
  (атрибут `Sdk` у `<Project>` або елемент `<Sdk Name=...>`, суфікс `/версія` ігнорується), немає `AzureFunctionsVersion`.
  Тест-проєкт (`IsTestProject=true` або `PackageReference Microsoft.NET.Test.Sdk`) — завжди `IsLibrary = true`.
- `AppSettingsLocalTarget.CanHandle`: `!IsLibrary` і поруч із `.csproj` є `appsettings.json` або `appsettings.local.json`.
- `UserSecretsTarget.CanHandle`: `!IsLibrary` або є `UserSecretsId`.
- Дефолт `IsLibrary = false` зберігає поведінку для фейків/старих викликів.

### U3. Позначка `//@global` у локальних файлах
- Обрано коментар у файлі, а не список у binding: позначка видима розробнику, її можна поставити/зняти руками,
  а `Microsoft.Extensions.Configuration.Json` (runtime і наш reader) коментарі пропускає.
- `LocalValue(string Key, string Value, ConfigScope Scope = ConfigScope.Service)`; `ToString` показує `Scope`, значення — `***`.
- Формат: рядковий коментар `//@global` у кінці рядка листового значення (після коми, якщо є):
  ```json
  {
    "Logging": {
      "LogLevel": "Information" //@global
    },
    "Api": {
      "Url": "https://x", //@global
      "Timeout": "30"
    }
  }
  ```
  (кома стоїть перед коментарем).
- Core, `Mapping/JsonProjection`:
  - `string ToNestedJsonText(IEnumerable<LocalValue> values)` — вкладений JSON із відступом 2 пробіли (як
    `WriteIndented`), листи зі `Scope == Global` отримують ` //@global`; без global-ключів результат дорівнює
    `ToNestedJson(values).ToJsonString(new() { WriteIndented = true })`.
  - `IReadOnlySet<string> ReadGlobalMarkers(string json)` — шляхи (`A:B`, регістр ігнорується) листів, за значенням
    яких одразу йде коментар `@global` (`Utf8JsonReader`, `CommentHandling.Allow`, `AllowTrailingCommas`; пробіли в
    коментарі ігноруються, `/*@global*/` теж приймається).
- `JsonFileTarget`: запис — `ToNestedJsonText`; читання — значення як і раніше через configuration provider, scope —
  `Global` для ключів із `ReadGlobalMarkers`.
- `SyncService`: pull пише `LocalValue` зі scope запису (`entry.Scope`, після `ScopeMerger`: service-override → `Service`);
  `ToState(local)` бере `LocalValue.Scope`. Тож push змінену позначену пару відносить до `global` (`TouchesGlobal`,
  потрібне підтвердження), без позначки — як раніше (service-override). Новий локальний ключ із позначкою → `Add` у `global`.
- Обмеження: `dotnet user-secrets set` перезаписує файл без коментарів — позначки зникають (ключі стають service).
  `FlatDictionary` (`local.settings.json`) поза MVP.

### U4. Відформатований JSON
- Усі `JsonFileTarget` (user-secrets, `appsettings.local.json`) пишуть через `ToNestedJsonText` — з відступами.

### U5. Конфіги у файлах проєкту + `.gitignore`
- `UserProfile(..., string? DefaultLabel = null, bool GitIgnoreLocalFiles = false)`; налаштування
  `gitignoreLocalFiles` (`true`/`false`) в `IUserSettingsService` (після `defaultLabel`), `azcfg config set gitignoreLocalFiles true`.
- Порт: `ILocalTarget.EnsureGitIgnoredAsync(LocalTargetBinding target, CancellationToken ct)` — робить файл цілі
  git-ignored, якщо він у репо:
  - `AppSettingsLocalTarget`: якщо `git check-ignore -q` вже ігнорує файл — нічого; інакше дописує рядок
    `appsettings.local.json` у `.gitignore` поруч із `.csproj` (створює файл; без дубля, якщо рядок
    `appsettings.local.json` чи `/appsettings.local.json` уже є; новий рядок із нового рядка).
  - `UserSecretsTarget`: no-op (поза репо).
- Core (вирішує; Infrastructure лише виконує), лише коли `GitIgnoreLocalFiles == true`:
  - `ProjectService.ResolveAsync` повертає опції з `IsSafeForSecrets = true` (файл буде проігноровано під час bind);
    оскільки `AppSettingsLocal` зареєстрована першою, неінтерактивний bind обирає файл проєкту — це і є «режим
    збереження в проєкті»;
  - `ProjectService.BindAsync` викликає `EnsureGitIgnoredAsync` для values і secrets цілей **до** перевірки безпеки;
    якщо ціль усе одно небезпечна (напр. поза git) — `InvalidOperationException`, як і раніше;
  - `SyncService.PullAsync` викликає `EnsureGitIgnoredAsync` для обох цілей до перевірки безпеки (binding, створений
    до увімкнення режиму).
- Вимкнено (за замовчуванням) — `EnsureGitIgnoredAsync` не викликається, `.gitignore` не змінюється.

## Наслідки
- Нові поля — з дефолтами, старі `profile.json`/`projects.json` читаються без міграції.
- Секрети не потрапляють у git: файл стає safe лише після запису в `.gitignore` і перевірки `git check-ignore`.
- Версія CLI — 0.3.0.
