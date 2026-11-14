# 0004. Конвенція ключів

Status: accepted

## Контекст
Один store містить конфіг багатьох сервісів і глобальні значення.

## Рішення
- Сервісні ключі: `<service-name>:<Key:Path>`; глобальні: `global:<Key:Path>`.
- Pull: `global:*` + `<service>:*`, префікс зрізається, сервісні перекривають global (з provenance).
- Push: за замовчуванням scope = сервіс; зміна global лише явно (секція `@global`) + окреме попередження.
- Секрети KV: `:` → `--`. local-цілі з форматом `NestedJson` (user-secrets, `appsettings.local.json`): шлях `A:B` →
  `{"A":{"B":...}}`; scope-префікс (`global`/сервіс) є лише в ключі App Configuration і в local JSON не потрапляє.
  `local.settings.json` (`LocalSettings`) — лише формат `FlatDictionary`: плоский словник рядків у `Values`, ключі
  зберігають повний шлях `A:B` як є, без вкладеності; вкладений JSON для цієї цілі заборонено.
- JSON-формат: `{ "@kv": "name" }` — KV reference; `{ "@kv": "name", "value": "..." }` — створити/оновити секрет.

## Примітка: формат local-цілі
Формат — властивість цілі (`LocalTargetBinding.Format`, ADR 0006), не глобальна настройка: `NestedJson` для
user-secrets і `appsettings.local.json` (MVP); `FlatDictionary` обов'язковий і єдиний для `local.settings.json`
(спроєктовано в контрактах, реалізація — не в MVP).

## Наслідки
- `KeyMapper`, `ScopeMerger`, `JsonProjection` — центральна логіка Core, покривається тестами першою.
