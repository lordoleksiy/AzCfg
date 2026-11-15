# 0006. Локальні цілі та прив'язка проєкту

Status: accepted

## Контекст
Розробник сам вирішує, куди йдуть значення, а куди — секрети: user-secrets, `appsettings.local.json` чи
`local.settings.json` (Azure Functions). Потрібен контракт, який:
- розрізняє кінд цілі та формат файлу (нема одного-до-одного — `local.settings.json` має особливий формат);
- дозволяє значенням і секретам іти в різні цілі (або в одну й ту саму);
- дає клієнту список варіантів із позначкою, чи ціль безпечна для секретів, і пам'ятає зроблений вибір.

## Рішення
### Цілі (decision 1)
- `LocalTargetKind { UserSecrets, AppSettingsLocal, LocalSettings }`.
- `LocalFileFormat { NestedJson, FlatDictionary }`.
- `LocalTargetBinding(Kind, ProjectPath, Format)` — `ProjectPath` це шлях до `.csproj`; валідація:
  - `LocalSettings` вимагає `FlatDictionary` (вкладений JSON для нього заборонений — Azure Functions Core
    Tools читає лише плоский словник рядків із `Values`);
  - `ProjectPath` не може бути порожнім.
- `UserSecrets` і `AppSettingsLocal` — MVP, формат `NestedJson` (сегменти шляху за `:` → вкладені об'єкти,
  без scope-префікса). `LocalSettings` — спроєктовано в контрактах, реалізація Infrastructure — пізніше.
- `appsettings.Development.json` як ціль прибрано з усіх документів; його замінює `appsettings.local.json`.

### Прив'язка проєкту (decision 2)
- Розробник вибирає цілі для значень і секретів один раз; вибір памʼятається (`projects.json`).
- `ProjectBinding(RepoPath, Service, ValuesTarget, SecretsTarget, VaultByEnvironment)` —
  `ValuesTarget`/`SecretsTarget` — `LocalTargetBinding`; можуть бути однією й тією самою ціллю.
- `LocalTargetOption(Target, Exists, IsSafeForSecrets)` — пропозиція клієнту. `IsSafeForSecrets = true` для
  `UserSecrets` (поза репо); для файлу в репо — лише якщо він у `.gitignore` і не затрекано git.
- `ProjectResolution(Candidates, IsConfident, TargetOptions, Existing)` — якщо `Existing != null`, клієнт
  використовує його без запитань (запам'ятаний вибір).
- `IProjectService.BindAsync(repoPath, service, valuesTarget, secretsTarget, ct)`.
- Безпека: `SecretsTarget` має бути безпечним для секретів. `BindAsync` відхиляє небезпечну ціль для
  секретів; `pull` перевіряє це ще раз безпосередньо перед записом (стан файлової системи може змінитися
  між bind і pull) і не пише нічого, якщо ціль стала небезпечною.

## Наслідки
- `ILocalTarget` (Core/Abstractions) працює з `LocalTargetBinding`/`LocalValue`, а не з рядковими словниками
  (деталі — ADR 0007, decision 4).
- Infrastructure реалізує `UserSecretsTarget`, `AppSettingsLocalTarget` зараз; `LocalSettingsTarget` — пізніше.
