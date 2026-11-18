# 0002. Окремий App Configuration на кожне середовище

Status: accepted; доповнено ADR 0008 (режим «один store, label на середовище» реалізовано — `LabelResolver`;
у store-режимі читаються всі labels, а не лише ключі без label)

## Контекст
Сьогодні dev / stage / prod мають окремі App Configuration. Згодом можливий один store з labels.

## Рішення
Усі use cases працюють лише з `ConfigEnvironment(Name, StoreEndpoint, Label?)`.
`IEnvironmentResolver` має дві стратегії: `SeparateStoresResolver` (Label = null) і `LabelResolver` (ADR 0008).
Мапінг stores на середовища: тег `environment`, потім суфікс назви (`-dev`, `-stg`, `-prod`).

## Наслідки
- Перехід на labels не зачіпає use cases.
- Apply в prod вимагає ввести назву середовища.
