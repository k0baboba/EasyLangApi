# Шаг 07. Только то, что пора повторить

Задача 4 «Фильтр и сортировка словаря» · тема: LINQ, лямбды · ~8 минут · в guide.md это задача 2

## Условие

Параметр `due=true`: вернуть только элементы, у которых `DueAt` не позже
текущего времени. Без параметра — все. Фильтр сочетается с `skip` и `take`.

Текущее время берите из `TimeProvider`, а не из `DateTime.UtcNow`: тест двигает
часы.

- Где: `Vocabulary/VocabularyEndpoints.cs`.
- Тест: `exercises/Exercises.Tests/Step07/DueFilterTests.cs`.

## Почитать

- [Лямбда-выражения](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/lambda-expressions)
- [TimeProvider](https://learn.microsoft.com/dotnet/standard/datetime/timeprovider-overview)

## Подсказка 1

Где в проекте уже есть запрос «DueAt не позже сейчас»? Посмотрите
`ReviewService.NextAsync`.

## Подсказка 2

`TimeProvider` можно получить параметром обработчика, как `AppDbContext`: он
зарегистрирован в DI.

## Подсказка 3

Тот же приём, что на шаге 05: `Where` дописывается к запросу только при
`due == true`, и до `Skip/Take`, а не после. Почему именно до?
