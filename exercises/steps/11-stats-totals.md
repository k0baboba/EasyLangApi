# Шаг 11. Статистика: сколько слов и сколько выучено

Задача 6 «Статистика повторений» · тема: SQL, LINQ · ~9 минут · в guide.md это задача 8

## Условие

Новый эндпоинт `GET /stats`, только для вошедших пользователей (как все
остальные). Ответ:

```json
{ "totalItems": 3, "knownItems": 2, "days": [ ... ] }
```

- `totalItems` — сколько элементов в словаре пользователя;
- `knownItems` — сколько из них выучено: `IntervalDays` не меньше
  `Review:KnownAfterDays` (класс `ReviewOptions`);
- `days` заполним на шаге 12, пока можно отдавать пустой массив.

- Где: новая папка `Stats/` по образцу `Vocabulary/`, регистрация маршрутов в `Program.cs`.
- Тест: `exercises/Exercises.Tests/Step11/StatsTotalsTests.cs`.

## Почитать

- [CountAsync и агрегаты в EF Core](https://learn.microsoft.com/ef/core/querying/complex-query-operators)
- [Шаблон параметров (IOptions)](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options)

## Подсказка 1

Как устроен `Vocabulary/VocabularyEndpoints.cs`: статический класс,
метод-расширение `Map...Endpoints`, вызов в `Program.cs`. Повторите эту форму.

## Подсказка 2

Где в проекте уже читают `KnownAfterDays`? Посмотрите `Review/ExampleLineFinder.cs`.

## Подсказка 3

Нужно ли здесь писать `Where(k => k.UserId == ...)`? Вспомните про глобальные
фильтры в `Data/AppDbContext.cs` и вопрос 16 из guide.md.
