# AzCfg

Локальний менеджер Azure App Configuration + Key Vault для .NET-розробників: двостороння синхронізація
конфігів між Azure та локальними файлами (local.settings.json, user-secrets, appsettings) і редагування
App Configuration у JSON. Core — class library, клієнт зараз CLI (`dotnet tool azcfg`), згодом UI.
Мова спілкування — українська; код, ідентифікатори й коміти — англійською.

## Команди
```
dotnet build AzCfg.slnx
dotnet test AzCfg.slnx
dotnet test tests/AzCfg.Core.Tests --filter "FullyQualifiedName~KeyMapper"
dotnet format AzCfg.slnx
dotnet run --project src/AzCfg.Cli -- <args>
```
Warnings = errors (`Directory.Build.props`). Версії пакетів лише в `Directory.Packages.props`.

## Структура і межі шарів
```
src/AzCfg.Core            модель, Mapping, Sync, Abstractions (порти), UseCases
src/AzCfg.Infrastructure  Azure SDK, файлова система, стан користувача, DI (AddAzCfg)
src/AzCfg.Cli             Spectre.Console.Cli — лише взаємодія з користувачем
tests/AzCfg.Core.Tests    основна маса тестів
tests/AzCfg.Infrastructure.Tests
```
- **Core ні на що не посилається**: жодного Azure SDK, файлового IO, консолі. Лише BCL + `ILogger`.
- **Core нічого не питає і не друкує** — повертає дані (`SyncPlan`, `ProjectResolution`), рішення приймає клієнт.
- Infrastructure реалізує порти з `Core/Abstractions`; Cli залежить від Core і Infrastructure.
- Усе, що виводить CLI, має мати `--json` еквівалент (серіалізація use-case record як є).
- Один тип на файл, file-scoped namespaces.

## Конвенції ключів
- Сервіс: `<service>:<Key:Path>`; глобальні: `global:<Key:Path>`.
- Pull: `global:*` + `<service>:*`, префікс зрізається, сервісні перекривають global.
- Push за замовчуванням лише scope сервісу; global — тільки явно через `@global`.
- KV-секрети: `:` → `--`. Формат — властивість локальної цілі (`LocalTargetBinding.Format`): `NestedJson`
  (сегменти шляху за `:`, без scope-префікса) для user-secrets/`appsettings.local.json`; `local.settings.json`
  — лише `FlatDictionary` (плоский словник `Values`, ключі `A:B` як є, без вкладеності).
- `{ "@kv": "name" }` → KV reference; з `"value"` → створити/оновити секрет.

## Безпека
- Auth лише `DefaultAzureCredential`.
- Секрети ніколи не пишуться у git-tracked файли.
- Запис в App Config з `If-Match: etag`; prod apply вимагає ввести назву середовища.
- Значення секретів ніколи не потрапляють у знімки (`snapshots`), плани, логи чи `--json` вивід —
  лише через `SecretString` (редагується до `"***"`) і fingerprint (`ISecretFingerprinter`).

## Правила роботи
- TDD для Core: спершу падаючий тест, потім код.
- Маленькі кроки: одна сутність/поведінка за раз; після кроку `dotnet build` + `dotnet test`.
- Не додавай бізнес-логіку поза поточним кроком MVP; не рефактор чужого коду без запиту.
- Нові залежності — лише через `Directory.Packages.props` і лише за потреби.
- Модель: sealed records, immutable; async методи приймають `CancellationToken`.

## Документація
- Повна архітектура, моделі, порти, CLI, порядок MVP: `docs/architecture.md`
- План MVP по задачах (брати одну через `/feature`): `docs/mvp-plan.md`
- Рішення (ADR): `docs/adr/` (0001 zero-config, 0002 stores per env, 0003 core never prompts,
  0004 key naming, 0005 KV discovery, 0006 local targets and binding, 0007 sync state and secret safety)

## Skills
- `/add-local-target` — новий `ILocalTarget` + тест + DI
- `/add-cli-command` — нова команда + `--json`
