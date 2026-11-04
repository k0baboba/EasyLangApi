# Шаг 16. Запасной разборщик

Задача 9 «Запасной разборщик» · тема: DI, декоратор · ~10 минут · в guide.md это задача 7

## Условие

Если задан ключ языковой модели, а она недоступна (5xx, неверный ключ), сейчас
пользователь получает 503. Сделайте так, чтобы при
`ExplainerUnavailableException` от `LlmExplainer` разбор делал
`DictionaryExplainer`. В ответе `source` будет `"dictionary"` — так
пользователь поймёт, откуда разбор.

`LookupService` не меняйте. Без ключа LLM `IPhraseExplainer` по-прежнему
должен быть `DictionaryExplainer`.

- Где: новый класс в `Explaining/`, регистрация в `Explaining/ExplainingServiceCollectionExtensions.cs`.
- Тест: `exercises/Exercises.Tests/Step16/FallbackTests.cs`.

## Почитать

- [Внедрение зависимостей в ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection)
- Паттерн «декоратор»: [refactoring.guru/ru/design-patterns/decorator](https://refactoring.guru/ru/design-patterns/decorator)

## Подсказка 1

Чем плох `try/catch` вокруг вызова в `LookupService`? Подумайте, сколько мест
придётся менять, если разборщик понадобится ещё где-то.

## Подсказка 2

Декоратор — класс, который реализует тот же интерфейс `IPhraseExplainer` и
внутри держит два других разборщика. Снаружи никто не знает, что он составной.

## Подсказка 3

Регистрация уже выбирает реализацию фабрикой `sp => ...`. Достаточно, чтобы
при включённой LLM фабрика возвращала ваш класс, собранный из
`sp.GetRequiredService<LlmExplainer>()` и `sp.GetRequiredService<DictionaryExplainer>()`.
Какое исключение ловить, а какое (отмену запроса) — пропускать?
