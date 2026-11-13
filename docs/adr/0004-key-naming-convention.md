# 0004. Конвенція ключів

Status: accepted

## Контекст
Один store містить конфіг багатьох сервісів і глобальні значення.

## Рішення
- Сервісні ключі: `<service-name>:<Key:Path>`; глобальні: `global:<Key:Path>`.
- Pull: `global:*` + `<service>:*`, префікс зрізається, сервісні перекривають global (з provenance).
- Push: за замовчуванням scope = сервіс; зміна global лише явно (секція `@global`) + окреме попередження.
- Секрети KV: `:` → `--`. local.settings.json (Functions): `:` → `__`.
- JSON-формат: `{ "@kv": "name" }` — KV reference; `{ "@kv": "name", "value": "..." }` — створити/оновити секрет.

## Наслідки
- `KeyMapper`, `ScopeMerger`, `JsonProjection` — центральна логіка Core, покривається тестами першою.
