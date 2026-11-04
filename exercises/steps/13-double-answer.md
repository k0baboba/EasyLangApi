# Шаг 13. Один показ — один ответ

Задача 7 «Двойной ответ» · тема: SQL, EF Core · ~10 минут · в guide.md это задача 5

## Условие

`ReviewService.AnswerAsync` читает показ, проверяет `AnsweredAt is null` и
пишет ответ. Два одновременных `POST /review/{id}` оба проходят проверку, и
оба получают 200. Принят должен быть ровно один, второй получает
409 Conflict — как сейчас при повторном ответе по очереди.

Тест придерживает `SaveChanges` так же, как на шаге 09.

- Где: `Review/ReviewService.cs`.
- Тест: `exercises/Exercises.Tests/Step13/DoubleAnswerTests.cs`.

## Почитать

- [Конфликты параллелизма в EF Core](https://learn.microsoft.com/ef/core/saving/concurrency)
- [ExecuteUpdate](https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete)

## Подсказка 1

Проверка «ещё не отвечено» и запись ответа сейчас — два отдельных шага, и
между ними может вклиниться соседний запрос. Нужно, чтобы проверка и запись
стали одной операцией в базе.

## Подсказка 2

Вариант А: `UPDATE ... SET AnsweredAt = ..., WasCorrect = ... WHERE Id = ?
AND AnsweredAt IS NULL` и проверить, сколько строк обновилось. В EF это
`ExecuteUpdateAsync`. Вариант Б: токен параллелизма на `ReviewEvent`.
Какой проще без миграции?

## Подсказка 3

`ExecuteUpdateAsync` пишет в базу сразу, мимо отслеживания. Значит, объект
`shown` в контексте об этом не знает. Как это повлияет на историю, которую
вы передаёте планировщику?
