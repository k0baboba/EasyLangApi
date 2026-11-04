# Шаг 08. Сортировка по выбору

Задача 4 «Фильтр и сортировка словаря» · тема: делегаты, Expression · ~20 минут

## Условие

Параметр `sort`:

- `recent` (по умолчанию) — сначала новые, как сейчас;
- `phrase` — по фразе от «a» к «z»;
- `due` — по `DueAt`, сначала самые ранние.

Неизвестное значение — 400. Сортировка сочетается с `due`, `skip`, `take`.
Не пишите четыре почти одинаковых запроса.

- Где: `Vocabulary/VocabularyEndpoints.cs`.
- Тест: `exercises/Exercises.Tests/Step08/SortTests.cs`.

## Почитать

- [Деревья выражений](https://learn.microsoft.com/dotnet/csharp/advanced-topics/expression-trees/)
- [Func и Expression<Func>: в чём разница для EF Core](https://learn.microsoft.com/ef/core/querying/client-eval)

## Подсказка 1

Что передаётся в `OrderBy`? Наведите курсор на метод у `IQueryable` и у
`IEnumerable` — типы параметров разные. Почему?

## Подсказка 2

Можно хранить «как сортировать» как значение: словарь или `switch`, который
по строке возвращает то, что умеет сортировать запрос. Подумайте, какой у
этого значения тип, если ключи сортировки разных типов (`string`, `DateTime`).

## Подсказка 3

Если тип ключа мешает, пусть по строке выбирается не ключ, а функция
`IQueryable<KnowledgeItem> → IQueryable<KnowledgeItem>`, которая сама вызывает
нужный `OrderBy`. Это делегат, и EF его не видит — он видит только результат.
