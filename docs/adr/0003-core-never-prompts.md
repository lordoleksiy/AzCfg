# 0003. Core нічого не питає і не друкує

Status: accepted

## Контекст
Один і той самий функціонал мають обслуговувати CLI, `--json` режим і майбутній UI.

## Рішення
`AzCfg.Core` — чиста логіка без Azure SDK і файлової системи; повертає дані (пропозиції, `SyncPlan`), рішення приймає клієнт.
Взаємодія з зовнішнім світом — через порти (`Abstractions/`), реалізації в `AzCfg.Infrastructure`.
Прогрес — `IProgress<SyncProgress>`, логування — `ILogger<T>`.

## Наслідки
- Core тестується без моків Azure та диска.
- Модель «пропозиція → рішення»: `ResolveAsync` → `BindAsync` → `PlanPushAsync` → `ApplyAsync`.
