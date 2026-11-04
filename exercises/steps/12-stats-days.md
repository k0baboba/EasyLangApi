# Шаг 12. Статистика по дням

Задача 6 «Статистика повторений» · тема: SQL, LINQ GroupBy · ~20 минут

## Условие

Поле `days` в `/stats` — ровно 7 элементов, от самого старого дня к сегодняшнему
(сутки по UTC, «сегодня» — по `TimeProvider`):

```json
{ "date": "2026-01-03", "answers": 1, "correct": 1 }
```

`answers` — ответы, данные в этот день (`ReviewEvent.AnsweredAt`), `correct` —
из них верные. Дни без ответов тоже есть, с нулями. Показы без ответа не
считаются. Чужие ответы не видны.

- Где: `Stats/`.
- Тест: `exercises/Exercises.Tests/Step12/StatsDaysTests.cs`.

## Почитать

- [Группировка в LINQ](https://learn.microsoft.com/dotnet/csharp/linq/standard-query-operators/grouping-data)
- [Какие GroupBy EF Core переводит в SQL](https://learn.microsoft.com/ef/core/querying/complex-query-operators#groupby)
- Функции даты SQLite: [date()](https://www.sqlite.org/lang_datefunc.html)

## Подсказка 1

Сначала напишите запрос руками в `sqlite3 easylang.db`: `SELECT date(AnsweredAt),
COUNT(*), ... FROM ReviewEvents WHERE ... GROUP BY ...`. Как посчитать
«верные» одним `SUM`?

## Подсказка 2

В LINQ группировка по дню — это группировка по `AnsweredAt.Date`. Включите лог
SQL и проверьте, ушла ли группировка в базу или EF вытащил все строки.

## Подсказка 3

База вернёт только дни, в которые были ответы. Семь дней с нулями проще
собрать в памяти: перечислить даты от «сегодня минус 6» до «сегодня» и для
каждой взять число из результата запроса или ноль.
