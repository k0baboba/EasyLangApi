# Шаг 03. Повтор при временном сбое словаря

Задача 2 «Повторные попытки» · тема: HttpClient · ~8 минут · в guide.md это задача 6

## Условие

Словарь иногда отвечает 5xx, а через секунду отвечает нормально. Сейчас
первый же 503 превращается в ответ 503 для пользователя. Сделайте так, чтобы
запрос к словарю повторялся при 5xx.

Тест создаёт `DictionaryExplainer` через тот же DI, что и приложение
(`AddPhraseExplainers`), поэтому подходит и обработчик в конвейере HttpClient,
и повтор внутри класса. Тест ждёт ответа до 20 секунд: делайте задержку между
попытками небольшой (сотни миллисекунд).

- Где: `Explaining/ExplainingServiceCollectionExtensions.cs` или `Explaining/DictionaryExplainer.cs`.
- Тест: `exercises/Exercises.Tests/Step03/RetryTests.cs`.

## Почитать

- [Устойчивые HTTP-приложения: Microsoft.Extensions.Http.Resilience](https://learn.microsoft.com/dotnet/core/resilience/http-resilience)
- [DelegatingHandler и конвейер HttpClient](https://learn.microsoft.com/aspnet/core/fundamentals/http-requests#outgoing-request-middleware)

## Подсказка 1

Есть два места, где можно повторять: внутри `DictionaryExplainer` (цикл вокруг
`GetAsync`) и в конвейере обработчиков `HttpClient`, который настраивается при
`AddHttpClient<...>()`. Какой вариант не требует трогать код самого класса?

## Подсказка 2

Пакет `Microsoft.Extensions.Http.Resilience` добавляет к результату
`AddHttpClient` метод, который вешает на клиент стратегию повторов. Ставится
командой `dotnet add package`.

## Подсказка 3

У стратегии повторов есть настройки: число повторов, задержка, тип задержки.
По умолчанию задержка — секунды. Уменьшите её, иначе тесты будут долгими.
Какие ответы считаются «стоит повторить», можно посмотреть в документации
к `HttpRetryStrategyOptions`.
