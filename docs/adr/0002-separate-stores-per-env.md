# 0002. Окремий App Configuration на кожне середовище

Status: accepted

## Контекст
Сьогодні dev / stage / prod мають окремі App Configuration. Згодом можливий один store з labels.

## Рішення
Усі use cases працюють лише з `ConfigEnvironment(Name, StoreEndpoint, Label?)`.
`IEnvironmentResolver` має дві стратегії: `SeparateStoresResolver` (зараз, Label = null) і `LabelResolver` (пізніше).
Мапінг stores на середовища: тег `environment`, потім суфікс назви (`-dev`, `-stg`, `-prod`).

## Наслідки
- Перехід на labels не зачіпає use cases.
- Apply в prod вимагає ввести назву середовища.
