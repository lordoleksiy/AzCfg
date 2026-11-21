# 0016. Дистрибуція UI: `azcfg ui` у dotnet tool

Status: accepted (2026-11-21). Реалізовано задачею X1 (`docs/ui-mvp-plan.md`). Спирається на ADR 0010 (AD-1: loopback,
токен, cookie) і на бюджет команд CLI (`docs/cli-roadmap.md` §1).

## Context
- UI (`src/AzCfg.Ui`, Blazor Server + Fluent UI + Monaco) існував лише як окремий застосунок (`dotnet run --project
  src/AzCfg.Ui`). Користувач CLI мав би ставити й версіонувати два артефакти з двома станами.
- Мета: один пакет `AzCfg`, одна версія, один user state (`~/.azcfg`), запуск командою `azcfg ui`.
- Ризик: dotnet tool пакується через `publish`; статичні веб-ресурси (`wwwroot`, `_content/<lib>/**`,
  `<app>.staticwebassets.endpoints.json`) потрапляють у пакет лише для проєкту з Web SDK.

## Decision
- `azcfg ui [--port N] [--no-browser] [--demo[=empty]] [--json]` — **launcher, а не workflow-команда**: +1 команда верхнього
  рівня (прийнято 2026-11-20, виняток із бюджету ADR 0013). `--demo` без значення = `full`; `--port` за замовчуванням 0
  (вільний порт ОС). Хост слухає лише `127.0.0.1`.
- Вивід: звичайно — лише login-URL з одноразовим токеном (як у `dotnet run`). З `--json` — один рядок
  `{"url":"http://127.0.0.1:<port>/login?t=<token>"}`; процес працює до Ctrl+C. Токен ніколи не потрапляє в `ILogger`.
- Межі: `AzCfg.Ui` віддає `UiHost.RunAsync(UiHostOptions, CancellationToken)`; `Program.cs` лишається для `dotnet run`.
  `AzCfg.Cli` посилається на `AzCfg.Ui`; `UiCommand` — тонкий (парсинг, Ctrl+C, виклик `UiHost`), без бізнес-логіки.
  `UiHostOptions` отримав `Json`.
- `AzCfg.Cli` переходить на `Microsoft.NET.Sdk.Web` (+ `<IsPackable>true`, бо Web SDK за замовчуванням вимикає pack).
  Явні `PackageReference` на `Microsoft.Extensions.DependencyInjection`/`Logging` прибрано з Cli: їх надає спільний
  фреймворк `Microsoft.AspNetCore.App` (інакше NU1605: транзитивні Fluent UI/BlazorMonaco тягнуть 10.0.15 проти 10.0.10).
  `EnableHotReloadInRuntimeConfigDevFile=false` у Cli: інакше в Debug обидва проєкти додають один і той самий
  hot-reload JS-модуль, і збірка падає («scoped css bundles do not have matching endpoints»).
- Корінь статичних ресурсів: коли UI запущено з інсталяції tool, `ContentRootPath`/`WebRootPath` беруться з
  `AppContext.BaseDirectory`, а `ApplicationName` — з збірки, що має `<name>.staticwebassets.endpoints.json` поряд
  (`AzCfg.Cli`, не `AzCfg.Ui`; `MapStaticAssets` шукає маніфест за іменем застосунку). Інакше (`dotnet run`, тести) —
  дефолти ASP.NET Core. Реалізація: `AzCfgUiApp.CreateWebOptions`.
- CI: скрипт `.github/scripts/pack-smoke.sh` (pack → `dotnet tool install --tool-path <tmp>` → `azcfg ui --demo --no-browser
  --json --port 5199` з чужого cwd → `GET /login?t=` = 302, по одному ресурсу `_content/Microsoft.FluentUI…` і
  `_content/BlazorMonaco` = 200 → kill) викликається з `ci.yml` і `publish-cli.yml` (перед `nuget push`).

## Spike result (2026-11-21, SDK 11.0.100 preview, runtime 10.0.15)
- Лише `ProjectReference` на `AzCfg.Ui` при `Microsoft.NET.Sdk` у Cli: пакет містить `AzCfg.Ui.dll`, але **без**
  `wwwroot` і без `staticwebassets.endpoints.json`, тож UI без стилів/Monaco. Тому потрібен Web SDK.
- З `Microsoft.NET.Sdk.Web`: у `tools/net10.0/any/` є `wwwroot/` (власні ресурси UI, `_framework/`, ~405 файлів
  `_content/**` для Fluent UI і BlazorMonaco, br/gz-варіанти) і `AzCfg.Cli.staticwebassets.endpoints.json`;
  `AzCfg.Cli.runtimeconfig.json` вимагає `Microsoft.AspNetCore.App` (спільний фреймворк SDK, окремо нічого ставити не треба).
- Інстальований tool, запущений з довільного cwd: `/login?t=` → 302, ресурси `_content/Microsoft.FluentUI…` і
  `_content/BlazorMonaco/jsInterop.js` → 200, без CDN. Smoke-скрипт пройшов локально (Windows, Git Bash).
- Побічний ефект: пакет містить і `AzCfg.Ui.exe` (apphost залежності). Безпечно, на розмір не впливає суттєво; не чіпаємо.

## Consequences
- Один пакет і одна версія для CLI та UI; `azcfg` тепер залежить від ASP.NET Core shared framework (є в .NET SDK, а
  користувачу tool потрібен лише .NET runtime з ASP.NET Core — для `dotnet tool install` це SDK, тож не вимога).
- Розмір пакета росте (~7.7 MB, переважно Monaco); CLI-команди не змінюються, їх старт не зачеплено (Web SDK
  не додає хостинг у CLI-шлях).
- Регресії пакування ловить CI до публікації; нова залежність UI з власними статичними ресурсами перевіряється тим самим
  скриптом лише вибірково (Fluent UI, Monaco) — нові бібліотеки з ресурсами варто додати в цикл скрипта.
- Реліз (версія, CHANGELOG) робить оркестратор, не X1.
