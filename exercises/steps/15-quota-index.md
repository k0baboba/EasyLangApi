# Шаг 15. Индекс для суточной квоты

Задача 8 «Индекс для квоты» · тема: SQL, индексы, миграции EF · ~10 минут · в guide.md это задача 9

## Условие

`LookupService.EnsureDailyQuotaAsync` при каждом разборе считает наблюдения
пользователя за сегодня. Индекса по `Lookup.CreatedAt` нет. Добавьте индекс
`Lookups(KnowledgeItemId, CreatedAt)` в модель и миграцию, которая создаст его
в базе.

- Где: `Data/AppDbContext.cs`, новая миграция в `Data/Migrations/`.
- Тест: `exercises/Exercises.Tests/Step15/QuotaIndexTests.cs`.

## Почитать

- [Индексы в EF Core](https://learn.microsoft.com/ef/core/modeling/indexes)
- [Миграции: dotnet ef migrations add](https://learn.microsoft.com/ef/core/managing-schemas/migrations/)
- [EXPLAIN QUERY PLAN в SQLite](https://www.sqlite.org/eqp.html)

## Подсказка 1

Возьмите SQL запроса квоты из лога и выполните `EXPLAIN QUERY PLAN <запрос>`
в `sqlite3`. Чем строки `SCAN` отличаются от `SEARCH ... USING INDEX`?

## Подсказка 2

Как объявлены другие составные индексы в `OnModelCreating`? Порядок колонок
важен: почему сначала `KnowledgeItemId`, а не `CreatedAt`?

## Подсказка 3

Инструмент `dotnet-ef` прописан в `dotnet-tools.json`: `dotnet tool restore`,
потом `dotnet ef migrations add <Имя> --output-dir Data/Migrations`. Откройте
созданный файл и прочитайте, что он сделает. Повторите `EXPLAIN QUERY PLAN`
после запуска и вставьте оба плана в описание коммита.
