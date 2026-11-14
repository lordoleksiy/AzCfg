# 0007. Нормалізований стан синхронізації та безпека секретів

Status: accepted

## Контекст
Синхронізація — двостороння (pull: remote→local; push: local→remote), а не лише one-way pull. Потрібен
один конвеєр для pull, push і edit, і проміжний контракт, який робить 3-way diff коректним незалежно від
того, з якого боку (Azure App Configuration, Key Vault чи локальний файл) прийшло значення. Секрети при
цьому не повинні потрапляти у знімки (`snapshots`), плани, логи чи `--json` вивід.

## Рішення
### Двостороння синхронізація (decision 3)
- `PushSource` — звідки береться локальна сторона push: `LocalFiles` (бібліотечний дефолт — push із
  прив'язаних локальних цілей) або `EditedJson(Json)` (push відредагованого JSON — так улаштований
  `azcfg edit`).
- `ISyncService.PlanPushAsync(binding, env, PushSource source, ct)` — заміняє стару сигнатуру з
  `string editedJson`.
- `ISyncService.DiffAsync(binding, env, ct) → ConfigDiff` — дані, якими майбутній UI порівнює local/remote/base
  (plan/apply для CLI; diff окремо — бо UI хоче бачити порівняння без обов'язкового plan).
- Один конвеєр для всіх напрямків: кожна сторона (remote, local, база з останнього pull) нормалізується в
  `ConfigState`, далі — 3-way diff (`ConfigDiff`), далі — `SyncPlan` для застосування.

### Нормалізований стан (decision 4)
- `StateValue`: `Plain(Value)` або `Secret(SecretUri?, Fingerprint)` — `Secret` ніколи не несе сам секрет,
  лише fingerprint; `SecretUri` — `null`, якщо секрет існує лише локально і ще не референсований.
- `StateItem(Key, Scope, Value)` — `Key` без scope-префікса (ADR 0004 / decision 6).
- `ConfigState(Items)` — словник за `Key`, ordinal.
- `DiffKind { Unchanged, LocalAdded, LocalModified, LocalDeleted, RemoteAdded, RemoteModified, RemoteDeleted, Conflict }`.
- `DiffItem(Key, Kind, Base?, Local?, Remote?)`, `ConfigDiff(Items)`.
- `LocalValue(Key, Value)` (Core/Model) — пара ключ/значення локального файлу; `ILocalTarget` працює з ним
  (не зі строковим словником):
  ```csharp
  public interface ILocalTarget
  {
      LocalTargetKind Kind { get; }
      bool CanHandle(ProjectInfo project);
      Task<LocalTargetOption> InspectAsync(LocalTargetBinding target, CancellationToken ct);
      Task<IReadOnlyList<LocalValue>> ReadAsync(LocalTargetBinding target, CancellationToken ct);
      Task WriteAsync(LocalTargetBinding target, IReadOnlyList<LocalValue> values, CancellationToken ct);
  }
  ```
  `LocalValue.ToString()`/`PrintMembers` не друкують `Value` (може бути секрет).
- Правила нормалізації (документ; реалізація — крок 5 MVP):
  - remote → `ConfigState`: `ConfigValue.ResolvedSecret` → `StateValue.Secret(SecretUri, fingerprint)`.
  - local → `ConfigState`: ключ є секретом, лише якщо база/remote вже знають його як KV-референс; інакше —
    `Plain` (нові секрети вводяться лише через `@kv` у edit JSON, не автодетектом локального файлу).
  - local-ключ scope = provenance із remote/бази; якщо його там немає — `Service`.
  - Правило scope для push: локальна зміна ключа, значення якого прийшло з `global`, створює override у
    scope сервісу (global ніколи не перезаписується мовчки); локальне видалення ключа, що прийшов з global,
    нічого не видаляє remote; `global` змінюється лише явно, через `@global` у edit JSON.

### Безпека секретів (decision 5)
- `SecretString` (Core/Model) — тримає значення, віддає його лише через `Reveal()`; `ToString()` → `"***"`;
  рівність за змістом (`IEquatable<SecretString>`); `[JsonConverter(typeof(SecretStringJsonConverter))]` —
  серіалізація пише `"***"`, десеріалізація завжди кидає виняток (секрет ніколи не відновлюється з
  серіалізованого плану/знімку/логу).
- `ConfigValue.ResolvedSecret(SecretUri, SecretString Value)`, `Change.SecretValue: SecretString?`.
- Знімки (`IUserStateStore`) не зберігають значення секретів:
  `LoadSnapshotAsync(repoPath, env, ct) → Task<ConfigState?>`,
  `SaveSnapshotAsync(repoPath, env, ConfigState state, ct)`.
  `GetOrCreateFingerprintKeyAsync(ct) → Task<byte[]>` — персональний випадковий ключ у каталозі стану
  користувача, ніколи в репо.
- `ISecretFingerprinter.Fingerprint(SecretString) → string` (Core/Abstractions);
  `HmacSecretFingerprinter` (Core/Security) — чиста реалізація Core: HMAC-SHA256 на копії ключа (відмовляє
  порожньому/короткому ключу, < 32 байт), вивід — lowercase hex. HMAC, а не звичайний SHA-256: секрети часто
  низькоентропійні, і неключований хеш можна підібрати брутфорсом; HMAC вимагає знання персонального ключа.

## Наслідки
- `SyncPlan`/`Change`, що містять секрет, безпечні для `ToString()`, логування і `System.Text.Json`
  серіалізації (у тому числі `--json` вивід CLI) — ніде немає значення секрету, лише `"***"` або fingerprint.
- `ThreeWayDiff` (реалізація нормалізації та власне diff-алгоритму) — наступний крок MVP, не частина цього
  рішення.
