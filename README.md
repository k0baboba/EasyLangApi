# EasyLangApi

Интеллектуальная система поддержки изучения английского языка. Пользователь смотрит
контент на английском, система разбирает незнакомые фразы **с учётом контекста
произведения** (название, говорящий, соседние реплики), складывает их в личный словарь
и возвращает на повторение.

Подробная постановка — в [SPEC.md](SPEC.md), состояние работы — в [STATUS.md](STATUS.md).

## Быстрый старт

Нужен [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Больше ничего:
база — файл SQLite, создаётся при первом запуске.

```bash
dotnet run
```

Приложение слушает `http://localhost:5057`. Файл базы `easylang.db` появится рядом с проектом.

Пройти весь поток можно запросами из [requests.http](requests.http) или через `curl`:

```bash
curl -F "file=@sample.srt" -F "title=Night Shift S01E01" http://localhost:5057/episodes
curl http://localhost:5057/episodes/1/lines
curl "http://localhost:5057/lines/3/explain?phrase=beat"
curl http://localhost:5057/vocabulary
curl http://localhost:5057/review/next
curl -X POST -H "Content-Type: application/json" -d '{"wasCorrect":true}' http://localhost:5057/review/1
```

## Тесты

```bash
dotnet test tests/EasyLangApi.Tests
```

Тесты не ходят в сеть: внешние API заменены подставными `HttpMessageHandler`,
у каждого интеграционного теста своя временная база SQLite.

## Поток и эндпоинты

| Метод | Путь | Что делает |
|---|---|---|
| `POST` | `/episodes` | Форма `multipart/form-data`: `file` — файл `.srt`, `title` — название (необязательно). Разбирает на реплики с таймингами |
| `GET` | `/episodes` | Список эпизодов |
| `GET` | `/episodes/{id}/lines` | Реплики эпизода |
| `GET` | `/lines/{id}/explain?phrase=...` | Разбор фразы с контекстом: название эпизода, говорящий, текущая реплика, две до и две после. Результат сохраняется |
| `GET` | `/vocabulary` | Сохранённые элементы и где их видели в последний раз |
| `GET` | `/review/next` | Следующий элемент на повторение (`204`, если повторять нечего). Каждый вызов — новый показ с `reviewId` |
| `POST` | `/review/{reviewId}` | Тело `{"wasCorrect": true}` — ответ на показ, пересчёт интервала |

Ошибки возвращаются в формате [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457):
`400` — неверный запрос, `404` — нет такой реплики/показа, `409` — на показ уже ответили,
`503` — внешний разборщик недоступен.

## Модель данных

Фраза — единица показа, слово (выражение) — единица учёта.

- `Episode`, `Line` — источник текста и реплики с таймингами.
- `Lookup` — наблюдение: какую фразу посмотрели, в какой реплике, что ответил разборщик.
- `KnowledgeItem` — элемент учёта: нормализованная фраза, интервал, срок повторения.
- `ReviewEvent` — сырая история показов и ответов. Ничего не удаляется и не агрегируется.

## Разбор фраз

Интерфейс `IPhraseExplainer`, две реализации:

- `DictionaryExplainer` — [dictionaryapi.dev](https://dictionaryapi.dev), бесплатно, без ключа.
  Работает по умолчанию. Контекст использует только для порядка значений.
- `LlmExplainer` — любой API, совместимый с OpenAI Chat Completions. Включается, если задан `Llm:ApiKey`.

## Конфигурация

`appsettings.json`, переменные окружения (`Llm__ApiKey=...`) или
[user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets).

| Ключ | По умолчанию | Смысл |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=easylang.db` | Файл SQLite |
| `Dictionary:BaseUrl` | `https://api.dictionaryapi.dev/api/v2/entries/en/` | Адрес словаря |
| `Llm:ApiKey` | пусто | Ключ API. Пусто — работает словарь |
| `Llm:BaseUrl` | `https://api.openai.com/v1/` | Адрес API |
| `Llm:Model` | `gpt-4o-mini` | Модель |
| `Llm:ExplanationLanguage` | `Russian` | На каком языке объяснять |

Ключи не коммитьте: для локальной работы удобнее `dotnet user-secrets set "Llm:ApiKey" "..."`.

## Структура

```
Data/          EF Core: контекст, сущности, миграции
Episodes/      загрузка субтитров, разбор SRT
Explaining/    IPhraseExplainer, словарь, языковая модель, сборщик промпта
Lookups/       сценарий «разобрать фразу»
Vocabulary/    нормализация фраз, список словаря
Review/        выдача на повторение и пересчёт интервала
Infrastructure/ обработка ожидаемых ошибок
tests/         xUnit
```

Миграции создаются локальным инструментом: `dotnet tool restore`, затем
`dotnet ef migrations add <Имя> -o Data/Migrations`.
