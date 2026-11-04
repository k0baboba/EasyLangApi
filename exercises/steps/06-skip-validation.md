# Шаг 06. Пропуск и проверка параметров

Задача 3 «Постраничный словарь» · тема: LINQ · ~15 минут

## Условие

Добавьте параметр `skip`: сколько элементов пропустить. Неверные значения —
400 Bad Request с ProblemDetails:

- `take` меньше 1 или больше 100;
- `skip` меньше 0.

`skip` за пределами списка — не ошибка, а пустой список.

- Где: `Vocabulary/VocabularyEndpoints.cs`.
- Тест: `exercises/Exercises.Tests/Step06/SkipAndValidationTests.cs`.

## Почитать

- [TypedResults и несколько типов ответа](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses#typedresults-vs-results)
- Как это уже сделано в проекте: `Lookups/LookupEndpoints.cs`.

## Подсказка 1

Метод сейчас возвращает `Task<List<VocabularyItem>>`. Чтобы иногда вернуть
400, тип ответа придётся поменять. Как это сделано в `LookupEndpoints`?

## Подсказка 2

Почему `Skip` без `OrderBy` бессмыслен? Есть ли `OrderBy` в запросе сейчас и
однозначен ли он, если у двух элементов одинаковое `FirstSeenAt`?

## Подсказка 3

Порядок важен: сначала `OrderBy`, потом `Skip`, потом `Take`. Проверки делайте
до похода в базу.
