# Шаг 05. Сколько элементов отдать

Задача 3 «Постраничный словарь» · тема: LINQ · ~8 минут · в guide.md это задача 1

## Условие

`GET /vocabulary` отдаёт весь словарь. Добавьте необязательный параметр
`take`: отдать не больше `take` элементов, начиная с самых новых. Без
параметра поведение прежнее — весь список.

- Где: `Vocabulary/VocabularyEndpoints.cs`, метод `ListAsync`.
- Тест: `exercises/Exercises.Tests/Step05/TakeTests.cs`.

## Почитать

- [Привязка параметров в minimal API](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding)
- [Take и Skip в LINQ](https://learn.microsoft.com/dotnet/csharp/linq/standard-query-operators/partitioning-data)

## Подсказка 1

Как в этом файле уже принимается необязательный параметр из строки запроса?
Посмотрите на `singleUnknownOnly` в `ExamplesAsync`.

## Подсказка 2

`Take` можно поставить до `ToListAsync` или после. Включите лог SQL
(`Microsoft.EntityFrameworkCore.Database.Command` в `appsettings.Development.json`)
и сравните запросы. Какой вариант тащит из базы лишнее?

## Подсказка 3

`IQueryable` можно собирать по частям: сохранить запрос в переменную и
дописать к нему `.Take(...)` только если параметр пришёл.
