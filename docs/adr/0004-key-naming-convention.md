# 0004. Конвенція ключів

Status: accepted

## Контекст
Один store містить конфіг багатьох сервісів і глобальні значення.

## Рішення
- Сервісні ключі: `<service-name>:<Key:Path>`; глобальні: `global:<Key:Path>`.
- Pull: `global:*` + `<service>:*`, префікс зрізається, сервісні перекривають global (з provenance).
- Push: за замовчуванням scope = сервіс; зміна global лише явно (секція `@global`) + окреме попередження.
- Секрети KV: `:` → `--`. local.settings.json: вкладені JSON-об'єкти, шлях `A:B` → `{"A":{"B":...}}`; scope-префікс (`global`/сервіс) є лише в ключі App Configuration і в local JSON не потрапляє; `__` не використовується.
- JSON-формат: `{ "@kv": "name" }` — KV reference; `{ "@kv": "name", "value": "..." }` — створити/оновити секрет.

## Примітка: формат local-цілі
- За замовчуванням — вкладений JSON (так працюють наші Functions: конфіг у `appSettings.local.json` / user-secrets).
- Формат у перспективі налаштовується per-ціль: вкладений JSON або плоскі ключі з `__` (напр. для `Values` у local.settings.json, де Functions Core Tools читає лише плоский словник). Зараз реалізовано тільки вкладений JSON; `__` — майбутня опція, не частина поточного кроку.

## Наслідки
- `KeyMapper`, `ScopeMerger`, `JsonProjection` — центральна логіка Core, покривається тестами першою.
