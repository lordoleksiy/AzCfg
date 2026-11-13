# 0001. Zero-config

Status: accepted

## Контекст
Розробникам потрібен швидкий старт без обовʼязкових файлів у репозиторіях сервісів.

## Рішення
Жодних обовʼязкових файлів у репо. Одноразовий майстер `azcfg init`, далі `pull / edit / push`.
Стан користувача — в `%APPDATA%/azcfg/` (Linux/macOS: `~/.config/azcfg/`). Опційний `.azcfg.yaml` лише перекриває автовизначення.
Автовизначення: Resource Graph для stores, префікси з dev store для сервісу, скан `*.csproj` для цілей.

## Наслідки
- Потрібен надійний Discovery з ручним fallback.
- Розробники пишуть в Azure напряму (без pipeline), тому plan → apply і ETag обовʼязкові.
