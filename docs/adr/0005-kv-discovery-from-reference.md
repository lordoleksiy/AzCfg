# 0005. Key Vault без хардкоду — vault з референсу

Status: accepted

## Контекст
Vault-и різні для сервісів і середовищ; хардкод або конфіг у репо суперечить zero-config.

## Рішення
- Читання: vault береться з URI в самому KV reference (`https://xxx.vault.azure.net/secrets/name`).
- Новий секрет, послідовно: (1) збережений вибір для проєкту/середовища → (2) найчастіший vault серед референсів сервісу →
  (3) серед `global:` референсів → (4) список з Resource Graph, ранжований (та сама RG, суфікс середовища), вибір користувача →
  (5) у майбутньому — створення vault.

## Наслідки
- `SyncPlan.RequiresVaultChoice` сигналізує клієнту, що потрібне рішення.
- Auth лише `DefaultAzureCredential`; RBAC: Key Vault Secrets User / Officer.
