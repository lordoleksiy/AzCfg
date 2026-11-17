# 0008. Режими середовищ (store / label), локальний ігнор і режими push

Status: accepted (issue #40)

## Контекст
- У режимі «окремий store на середовище» (ADR 0002) `AppConfigStore` читав лише ключі **без label**. Реальні
  stores містять ключі з label (напр. `Development`), назва якого не збігається з назвою середовища, яку
  користувач ввів у `init` (`dev`). Результат: 0 префіксів сервісу, `pull` падає з оманливою помилкою.
- Потрібен другий режим: один спільний store, середовища розділені label-ами.
- Розробнику потрібно тримати частину ключів лише локально (особливо `global:*`), не видаляючи їх ні в Azure,
  ні локально, і щоб `pull` їх не перетирав, а `push` не чіпав.
- `push` за замовчуванням видаляв remote-ключі, видалені локально, — це небезпечно для щоденного флоу.

## Рішення
### Режими середовищ (профіль користувача)
- `EnvironmentMode { StorePerEnvironment, LabelPerEnvironment }`;
  `UserProfile(Environments, Mode = StorePerEnvironment, DefaultLabel = null)` — це і є глобальні (per-user)
  налаштування: зберігаються в `profile.json` через `IUserStateStore` (окремий `settings.json` не потрібен —
  профіль уже per-user, не per-project).
- `ConfigEnvironment.Label` у **профілі**: store-режим — необов'язковий label для нових ключів цього store
  (перекриває `DefaultLabel`); label-режим — label середовища (якщо порожній — береться назва середовища).
- Runtime `ConfigEnvironment(Name, StoreEndpoint, Label, IsProtected, WriteLabel = null)` після resolver-а:
  - `Label` — селектор читання: `null` = **усі** labels (store-режим); інакше — ключі з цим label плюс
    ключі без label як fallback (label середовища перемагає).
  - `WriteLabel` — label для нових ключів.
- `SeparateStoresResolver`: `Label = null`, `WriteLabel = env.Label ?? profile.DefaultLabel`.
  `LabelResolver`: `Label = WriteLabel = env.Label ?? env.Name`. `ProfileEnvironmentResolver` (DI) обирає
  стратегію за `profile.Mode`.
- `ConfigEntry.Label` — label конкретного setting-а в store. `LabelSelector` (Core) зводить дублікати
  `(Scope, Key)` з різними labels до одного запису:
  - label-режим: `env.Label` > без label; пара «без label + label середовища» — норма, без попередження;
  - store-режим: `WriteLabel` > без label > перший за ordinal; дублікат — **не помилка**, а
    `SyncWarning(DuplicateLabels)`. Локально пишеться обрана копія; її ж оновлює push.
- Запис: Core ставить label у `Change.After/Before` (`Before.Label` для Update/Delete, `WriteLabel` для Add);
  Infrastructure пише рівно `entry.Label`. У label-режимі ключ, що існує лише без label (або під іншим label),
  **не змінюється**: зміна стає `Add` з label середовища (override) + `SyncWarning(LabelOverride)`; локальне
  видалення такого ключа нічого не видаляє.

### Попередження як дані
- `SyncWarning(Kind, Key, Message)`, `SyncWarningKind { DuplicateLabels, LabelOverride, DeleteSkipped }`.
- `PullResult.Warnings`, `SyncPlan.Warnings` (init-властивості, за замовчуванням `[]`). Core нічого не друкує;
  CLI показує жовтим, `--json` серіалізує як є.

### Локальний ігнор
- `ProjectBinding.Ignore` — список шаблонів, per-user (`projects.json`), не в репо.
- Шаблон: необов'язковий префікс `global:` (лише ключі зі scope `Global`), далі шлях ключа з `*` як
  wildcard (будь-які символи, включно з `:`), регістр ігнорується. Приклади: `Logging:*`, `global:*`,
  `global:Serilog:*`, `*:Password`, `ConnectionStrings:Local`.
- Scope ключа для матчингу — provenance: remote → base → local (`Service`).
- Ігнорований ключ — повністю власність локального файлу:
  - `pull` не пише його remote-значення, зберігає поточне локальне значення (якщо є) у тій самій цілі, не
    включає у знімок і не блокує pull через нього; `PullResult.Ignored` — список таких ключів;
  - `push`/`plan`/`diff` (джерело `LocalFiles`) його не бачать — ні Add, ні Update, ні Delete;
  - `edit`/`promote` ігнор не застосовують (явне редагування).
- `azcfg ignore add|remove|list` нічого не видаляє ні локально, ні в Azure.

### Режими push
- `PushSource.LocalFiles(Delete = false)`:
  - за замовчуванням **upsert**: лише Add/Update змінених ключів (незмінені не оновлюються — 3-way diff);
    локально видалені ключі **не** видаляються remote, а повертаються як `SyncWarning(DeleteSkipped)`;
  - `Delete = true` (`azcfg push --delete`, `azcfg plan --delete`) — плюс Delete.

### Сервіс проєкту
- `pull/use --service <name>` прив'язує проєкт без автовизначення; порожній список кандидатів дає помилку
  з підказкою `--service`.

### CLI
- `azcfg init [--mode stores|labels] [--store <endpoint>] [--label <default>]` — режим обирається в init;
  label-режим пропонує labels, знайдені в store (`IConfigStore.ListLabelsAsync`).
- `azcfg config list | get <key> | set <key> <value>` — `mode`, `defaultLabel`, `env.<name>.label`,
  `env.<name>.endpoint`, `env.<name>.protected`; `--json` для всіх.

## Відкриті питання (рішення за замовчуванням)
- `--default` з issue #40 не додано: глобальні налаштування — це профіль, проєкт їх не обирає; `--yes` уже
  прив'язує проєкт без питань.
- Конфлікт label-режиму не потребує інтерактивного кроку: інші labels ніколи не змінюються (override + warning).

## Наслідки
- Store-режим працює з будь-якими labels у store, незалежно від назви середовища.
- Перехід store → label — `azcfg config set mode labels` + labels середовищ; use cases не змінюються.
- `push` за замовчуванням безпечний (без видалень).
