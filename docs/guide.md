# Руководство по проекту EasyLangApi

Для студента, который знает C# на базовом уровне и будет защищать этот проект
на собеседовании и на ВКР.

Цель руководства — чтобы вы могли **объяснить и переписать сами любую строку**
проекта. Поэтому здесь нет готового кода для доработок. Есть порядок чтения,
разбор устройства, вопросы для самопроверки и задачи, которые нужно сделать
самостоятельно.

> Руководство написано по ветке `prototype` (коммит `028dd32`). Ссылки вида
> `Lookups/LookupService.cs:30` указывают на эту версию; после правок номера
> строк могут сдвинуться. Тесты при написании руководства не запускались
> (в среде не было .NET SDK). Сведения о том, что проверено, взяты из
> `STATUS.md`: там указано 126 зелёных тестов.

## Содержание

1. [Порядок чтения файлов](#1-порядок-чтения-файлов)
2. [Путь одного запроса: от эндпоинта до базы](#2-путь-одного-запроса-от-эндпоинта-до-базы)
3. [Архитектурные решения и альтернативы](#3-архитектурные-решения-и-альтернативы)
4. [30 вопросов собеседования с ответами](#4-30-вопросов-собеседования-с-ответами)
5. [10 задач на доработку](#5-10-задач-на-доработку)

---

## 1. Порядок чтения файлов

Читайте **по слоям сверху вниз и по одному сценарию за раз**, а не по папкам
в алфавитном порядке. После каждого шага закройте файл и попробуйте
пересказать вслух, что он делает. Не получается — читайте снова.

### Шаг 0. Зачем всё это (15 минут)

| Файл | На что смотреть |
|---|---|
| `SPEC.md` | Поток из шести шагов и модель данных. Запомните фразу «фраза — единица показа, слово — единица учёта» |
| `README.md` | Как запустить, какие эндпоинты есть |
| `STATUS.md` | Что проверено, а что только написано. На защите важно не выдать непроверенное за работающее |

### Шаг 1. Точка входа (20 минут)

| Файл | На что смотреть |
|---|---|
| `Program.cs` | Две половины: до `builder.Build()` идёт регистрация сервисов (DI), после — конвейер middleware и маршруты. Для каждой строки `AddScoped/AddSingleton` ответьте, почему выбрано это время жизни |
| `appsettings.json` | Все настройки. Сопоставьте каждую секцию с классом `*Options` |
| `EasyLangApi.csproj` | Зависимости и почему из сборки исключены `tests/`, `client/`, `docs/` |

### Шаг 2. Данные (30 минут)

| Файл | На что смотреть |
|---|---|
| `Data/Entities.cs` | Семь сущностей. Нарисуйте на бумаге связи между ними |
| `Data/AppDbContext.cs` | Индексы, глобальные фильтры по пользователю, `OnDelete(Restrict)` для истории, конвертер UTC |
| `Data/DatabaseExtensions.cs` | Миграции при старте |
| `Data/Migrations/*_InitialCreate.cs` | Какой SQL получается из модели. Посмотрите, во что превратились индексы |
| `Data/Migrations/*_AddUsers.cs` | Как старые данные переехали к служебному пользователю `legacy@localhost` |

### Шаг 3. Сценарий «загрузить субтитры» (30 минут)

`Episodes/EpisodeEndpoints.cs` → `Episodes/SrtTextSource.cs` →
`Episodes/SrtParser.cs` → `Episodes/EpisodeImporter.cs`.
Параллельно читайте `tests/.../Unit/SrtParserTests.cs`: тесты показывают,
какие крайние случаи учтены.

### Шаг 4. Сценарий «разобрать фразу» — главный (60 минут)

1. `Lookups/LookupEndpoints.cs` — проверка входа.
2. `Lookups/LookupService.cs` — сценарий целиком.
3. `Explaining/IPhraseExplainer.cs`, `PhraseContext.cs`, `Explanation.cs` — контракт.
4. `Explaining/ExplainingServiceCollectionExtensions.cs` — как выбирается реализация.
5. `Explaining/DictionaryExplainer.cs` — реализация по умолчанию: `HttpClient`, `async/await`, обработка ошибок.
6. `Explaining/ExplainerUnavailableException.cs` + `Infrastructure/KnownExceptionHandler.cs` — как исключение становится ответом 503.
7. `Vocabulary/PhraseNormalizer.cs` — по какому ключу ищется `KnowledgeItem`.
8. `Episodes/SpeakerParser.cs` — откуда берётся `Speaker`.

Тесты к сценарию: `Api/FlowTests.cs`, `Unit/DictionaryExplainerTests.cs`,
`Unit/PhraseNormalizerTests.cs`.

### Шаг 5. Сценарий «повторить» (45 минут)

`Review/ReviewEndpoints.cs` → `Review/ReviewService.cs` →
`Review/IReviewScheduler.cs` → `Review/FixedIntervalScheduler.cs` +
`Review/IntervalLadder.cs`. Затем `Review/ExampleLineFinder.cs` и
`Review/SingleUnknownSelector.cs` (отбор «i + 1»).

`Review/AdaptiveScheduler.cs` оставьте на отдельный день: там математика
(см. вопросы 24–26).

### Шаг 6. Языковая модель (40 минут)

`Explaining/LlmExplainer.cs` → `PromptBuilder.cs` → `LlmResponseParser.cs`
→ `ExplainerOptions.cs`. Тесты: `Unit/LlmExplainerTests.cs`,
`Infrastructure/StubHttpHandler.cs` — как проверить HTTP-клиент без сети.

### Шаг 7. Распознавание (30 минут)

`Recognition/RecognitionEndpoints.cs` → `ImageFormat.cs` →
`ImageTextSource.cs` → `TesseractRecognizer.cs` → `RecognitionExceptions.cs`.

### Шаг 8. Пользователи и квоты (60 минут)

`Auth/AuthServiceCollectionExtensions.cs` → `AuthEndpoints.cs` →
`AuthService.cs` → `TokenService.cs` → `SigningKeyProvider.cs` →
`ICurrentUser.cs`. Затем `Quotas/*`. Тесты: `Api/AuthTests.cs`,
`Api/IsolationTests.cs`, `Api/QuotaTests.cs`.

### Шаг 9. Тестовая инфраструктура (30 минут)

`tests/.../Infrastructure/ApiFactory.cs` — как приложение поднимается в
памяти, со своей базой и подставными часами. `FakeExplainer.cs`,
`FakeRecognizer.cs`, `TextImages.cs`.

### Шаг 10. Окружение (20 минут)

`Dockerfile`, `docker-compose.yml`, `.github/workflows/ci.yml`.
Клиент `client/` — по желанию: его можно генерировать, на защите основной
акцент на сервере.

### Темы, которые нужно подтянуть по дороге

| Когда встретите | Что почитать |
|---|---|
| `async`, `await`, `Task`, `CancellationToken` (везде) | [Асинхронное программирование в C#](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/) |
| `HttpClient`, `AddHttpClient` (шаг 4) | [IHttpClientFactory](https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory) |
| `.Where(...).Select(...)`, лямбды (шаги 2–5) | [LINQ](https://learn.microsoft.com/dotnet/csharp/linq/) |
| `try/catch ... when`, свои исключения (шаг 4) | [Исключения: рекомендации](https://learn.microsoft.com/dotnet/standard/exceptions/best-practices-for-exceptions) |
| Индексы, `LIKE`, SQL из миграций (шаг 2) | [SQLite: Query Planning](https://www.sqlite.org/queryplanner.html) |
| `DbContext`, `Include`, `SaveChangesAsync` | [EF Core: начало работы](https://learn.microsoft.com/ef/core/get-started/overview/first-app) |
| `AddScoped`, `AddSingleton` (шаг 1) | [Внедрение зависимостей в ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection) |

---

## 2. Путь одного запроса: от эндпоинта до базы

Разберём главный запрос: `GET /lines/42/explain?phrase=beat` с заголовком
`Authorization: Bearer <токен>`.

### 2.1. Общая картина

```mermaid
sequenceDiagram
    participant C as Клиент
    participant MW as Middleware
    participant E as LookupEndpoints
    participant S as LookupService
    participant DB as AppDbContext / SQLite
    participant X as DictionaryExplainer
    participant API as dictionaryapi.dev

    C->>MW: GET /lines/42/explain?phrase=beat
    MW->>MW: Authentication: токен → ClaimsPrincipal (sub = userId)
    MW->>MW: Authorization: FallbackPolicy требует входа
    MW->>MW: RateLimiter: окно 30 запросов/мин на пользователя
    MW->>E: ExplainAsync(id=42, phrase="beat", ...)
    E->>E: проверка phrase (пустая? длиннее 100?)
    E->>S: ExplainAsync(42, "beat")
    S->>DB: Line 42 + Episode (фильтр по владельцу)
    DB-->>S: строка или null → 404
    S->>DB: COUNT Lookups за сегодня → квота
    S->>DB: соседние реплики Index±2
    S->>X: ExplainAsync("beat", context)
    X->>API: GET entries/en/beat
    API-->>X: JSON или 404
    X-->>S: Explanation
    S->>DB: найти KnowledgeItem по "beat"
    S->>DB: INSERT KnowledgeItem? + INSERT Lookup (одна транзакция SaveChanges)
    S-->>E: LookupResult
    E-->>C: 200 OK + JSON
```

### 2.2. По шагам

**1. Middleware** (`Program.cs`, блок после `builder.Build()`).
Запрос проходит конвейер в порядке регистрации:

- `UseExceptionHandler()` — внешний слой: ловит всё, что вылетит ниже, и
  отдаёт ProblemDetails. Стоит первым, потому что ловит только то, что
  находится *после* него.
- `UseStatusCodePages()` — пустые ответы 404/401 получают тело.
- `UseAuthentication()` — читает заголовок `Authorization`, проверяет
  подпись и срок JWT, кладёт пользователя в `HttpContext.User`.
- `UseAuthorization()` — применяет политику. Эндпоинт не помечен
  `AllowAnonymous`, поэтому срабатывает `FallbackPolicy` из
  `Auth/AuthServiceCollectionExtensions.cs`: без входа будет 401.
- `UseRateLimiter()` — стоит **после** аутентификации, потому что ключ
  лимита — id пользователя из токена (`Quotas/RateLimitingExtensions.cs`).
  Политику `expensive` подключает сам эндпоинт: `.RequireRateLimiting(...)`.

**2. Привязка параметров** (`Lookups/LookupEndpoints.cs`).
Minimal API сам разбирает аргументы метода `ExplainAsync`:
- `int id` — из маршрута `{id:int}`; ограничение `:int` даёт 404 на `/lines/abc/explain`;
- `string? phrase` — из строки запроса;
- `LookupService lookups` — из DI-контейнера;
- `CancellationToken` — отменяется, если клиент оборвал соединение.

Тип возврата `Results<Ok<LookupResult>, NotFound, ProblemHttpResult>`
перечисляет все возможные ответы. Компилятор не даст вернуть что-то другое,
а OpenAPI-описание получается точным.

**3. Проверка входа** — там же. Пустая фраза или фраза из одних знаков
препинания отклоняется с 400. Проверка идёт через `PhraseNormalizer`,
а не через `string.IsNullOrWhiteSpace`. Подумайте, почему (подсказка: что
получится из фразы `"!!!"`).

**4. Сервис** (`Lookups/LookupService.cs`, метод `ExplainAsync`).

- Создание. `LookupService` зарегистрирован как `Scoped`: на каждый запрос
  создаётся новый экземпляр со своим `AppDbContext`. Зависимости приходят
  через первичный конструктор `LookupService(AppDbContext db, ...)`.
- Реплика. `db.Lines.Include(l => l.Episode).FirstOrDefaultAsync(...)`
  превращается в один `SELECT ... JOIN`. Глобальный фильтр из
  `AppDbContext` сам дописывает условие «эпизод принадлежит текущему
  пользователю». Чужая реплика вернётся как `null`, и наружу уйдёт 404.
- Квота. `EnsureDailyQuotaAsync` считает `Lookup` за текущие сутки по UTC.
  Если лимит исчерпан, бросает `QuotaExceededException`, и
  `KnownExceptionHandler` превращает её в 429.
- Контекст. `BuildContextAsync` делает один запрос по составному индексу
  `(EpisodeId, Index)` и разбивает результат на «до» и «после» в памяти.
- Объяснение. `explainer.ExplainAsync(...)` — сервису неизвестно, словарь
  это или языковая модель: он видит только `IPhraseExplainer`.

**5. Внешний HTTP** (`Explaining/DictionaryExplainer.cs`, `FetchAsync`).

- `HttpClient` получен через `AddHttpClient<DictionaryExplainer>`: базовый
  адрес и таймаут заданы при регистрации.
- Ответ 404 возвращается как `null`. Это нормальная ситуация: слова нет в
  словаре.
- Обрыв сети (`HttpRequestException`), таймаут (`TaskCanceledException`,
  когда отменил не клиент), код 5xx и битый JSON заворачиваются в
  `ExplainerUnavailableException`. Наружу она уходит как **503**: «сервис
  временно недоступен, повторите».

**6. Запись** — снова `LookupService`.

- `KnowledgeItem` ищется по нормализованной фразе. Если не найден,
  создаётся с `DueAt = now`, то есть его можно повторить сразу.
- `Lookup` создаётся всегда: это наблюдение «посмотрел вот это вот здесь».
- Один `SaveChangesAsync` записывает оба объекта в одной транзакции.
  EF сам расставит внешние ключи, потому что объекты связаны навигационными
  свойствами (`Line = line`, `KnowledgeItem = item`), а не через id.

**7. Ответ.** `TypedResults.Ok(result)` сериализуется в JSON в camelCase.

### 2.3. Что получится в SQL

Включите логирование команд: в `appsettings.Development.json` задайте
`"Microsoft.EntityFrameworkCore.Database.Command": "Information"`. Затем
выполните запрос из `requests.http` и найдите в консоли:

1. `SELECT` реплики с `INNER JOIN "Episodes"` и условием на `UserId`;
2. `SELECT COUNT(*)` для квоты;
3. `SELECT` соседей с `BETWEEN`-подобным условием на `Index`;
4. `SELECT` элемента по `PhraseNormalized`;
5. `INSERT` в `KnowledgeItems` (если элемент новый) и `INSERT` в `Lookups`.

**Упражнение:** посчитайте, сколько обращений к базе делает один разбор.
Какие из них можно объединить, а какие нельзя и почему?

### 2.4. Если что-то пошло не так

| Ситуация | Где решается | Код |
|---|---|---|
| Нет токена или он просрочен | `UseAuthentication` + `FallbackPolicy` | 401 |
| Больше 30 запросов в минуту | `UseRateLimiter`, `OnRejected` | 429 + `Retry-After` |
| `phrase` пустая или длинная | `LookupEndpoints` | 400 |
| Реплика чужая или её нет | глобальный фильтр → `null` | 404 |
| Квота на сутки исчерпана | `QuotaExceededException` → `KnownExceptionHandler` | 429 |
| Словарь недоступен | `ExplainerUnavailableException` → `KnownExceptionHandler` | 503 |
| Ошибка в нашем коде | `UseExceptionHandler` | 500 |

Заметьте: при 503 **ничего не сохраняется**, потому что исключение
вылетает до `SaveChangesAsync`.

---

## 3. Архитектурные решения и альтернативы

Для каждого решения: **что сделано**, **почему**, **альтернатива** и
**когда альтернатива лучше**. На защите почти всегда спрашивают «а почему не
X?», поэтому знать вторую половину так же важно, как первую.

### 3.1. Монолит на minimal API, папки по функциям

- **Сделано.** Один проект, папки по сценариям (`Episodes/`, `Lookups/`,
  `Review/`...), эндпоинты — статические методы `Map...Endpoints`.
- **Почему.** Прототип, один разработчик, `SPEC.md` запрещает микросервисы.
  Всё, что относится к сценарию, лежит рядом.
- **Альтернативы.** Контроллеры MVC (`[ApiController]`); слоистая
  архитектура в отдельных проектах (`Domain`, `Application`,
  `Infrastructure`); вертикальные срезы с MediatR.
- **Когда лучше.** Контроллеры удобны, когда команда к ним привыкла и нужны
  фильтры MVC. Отдельные проекты окупаются, когда запрет «домен не знает про
  EF» нужно обеспечить компилятором, а не договорённостью. Для 2500 строк это
  избыточно.

### 3.2. Интерфейс + несколько реализаций, выбор в конфигурации

- **Сделано.** `IPhraseExplainer` (словарь / LLM), `ITextRecognizer`
  (Tesseract / подставной в тестах), `IReviewScheduler` (фиксированный /
  адаптивный), `ITextSource` (srt / изображение).
- **Почему.** Три выигрыша сразу: плавная деградация (нет ключа → словарь),
  подмена в тестах (`FakeExplainer`), эксперимент для ВКР (два планировщика
  на одних данных).
- **Альтернативы.** `if (settings.UseLlm) ... else ...` внутри сервиса;
  паттерн «стратегия» без DI; keyed services
  (`AddKeyedScoped<IPhraseExplainer, LlmExplainer>("llm")`).
- **Когда лучше.** `if` проще, пока реализаций две и они никогда не
  подменяются в тестах. Keyed services удобны, если нужны *обе* реализации
  одновременно, например «спросить LLM, а при ошибке — словарь».

### 3.3. Выбор реализации — фабрикой при регистрации

- **Сделано.** `AddScoped<IPhraseExplainer>(sp => enabled ? Llm : Dictionary)`
  и такой же `switch` для планировщика.
- **Почему.** Решение принимается в одном месте. Сервисы о нём не знают.
- **Альтернатива.** Регистрировать по условию: `if (enabled)
  services.AddScoped<IPhraseExplainer, LlmExplainer>()`.
- **Разница.** Фабрика читает `IOptions` в момент разрешения сервиса,
  поэтому тесты могут подменить конфигурацию через `WithSetting`. Условие
  при регистрации читает конфигурацию раньше, при старте. Это одна из
  причин, по которой так сделано (см. комментарий про строку подключения в
  `Program.cs`).

### 3.4. Наблюдение отдельно от элемента знания

- **Сделано.** `Lookup` — «посмотрел фразу X в реплике Y», `KnowledgeItem` —
  «отслеживаю X». Связь — внешний ключ `Lookup.KnowledgeItemId`; в
  `SPEC.md` его не было, он добавлен.
- **Почему.** Одну фразу смотрят в разных сценах. Если хранить одну
  запись, теряются все контексты, кроме последнего. Если хранить только
  наблюдения, непонятно, где держать интервал повторения.
- **Альтернатива к внешнему ключу.** Связывать по строке:
  `Lookup.Phrase` нормализовать и искать `KnowledgeItem.PhraseNormalized`.
- **Чем хуже.** Поменяете правило нормализации (например, начнёте убирать
  артикли) — и старые наблюдения «отвалятся» от своих элементов.

### 3.5. История ответов не удаляется и не агрегируется

- **Сделано.** Каждый показ — строка `ReviewEvent`, ответ дописывается в
  неё же. `OnDelete(DeleteBehavior.Restrict)` не даёт удалить элемент вместе
  с историей.
- **Почему.** Адаптивный планировщик учится на сырой истории. Если хранить
  только «счётчик верных», нельзя заново пересчитать модель с другими
  параметрами — а для ВКР это и есть эксперимент.
- **Альтернатива.** Хранить агрегаты (`CorrectCount`, `LastReviewedAt`) в
  `KnowledgeItem`.
- **Когда лучше.** Когда история огромная, а модель зафиксирована. Обычный
  компромисс — хранить и то, и другое: агрегаты для быстрых запросов, сырые
  события для пересчёта.

### 3.6. SQLite + EF Core + миграции при старте

- **Сделано.** Файл `easylang.db`, `MigrateAsync()` в `Program.cs`.
- **Почему.** Требование «`dotnet run` с чистого клона» без Docker и
  сервера БД.
- **Альтернативы.** PostgreSQL; Dapper вместо EF; миграции отдельным шагом
  (`dotnet ef database update`, migration bundle, SQL-скрипт в CI).
- **Когда лучше.** PostgreSQL — при нескольких экземплярах приложения и
  параллельной записи: SQLite блокирует запись на весь файл. Миграции
  отдельным шагом — в продакшене, где при старте нескольких экземпляров
  миграции могут побежать одновременно, а у приложения не должно быть прав
  на изменение схемы. Dapper — когда хочется писать SQL руками и видеть
  каждый запрос.

### 3.7. Разделение данных глобальными фильтрами EF

- **Сделано.** `HasQueryFilter(e => e.UserId == CurrentUserId)` для
  `Episode` и `KnowledgeItem`. `Line`, `Lookup` и `ReviewEvent` фильтруются
  через навигацию.
- **Почему.** Нельзя забыть `Where(UserId == ...)` в новом запросе:
  фильтр добавляется сам.
- **Альтернативы.** Явный `Where` в каждом запросе; отдельная база на
  пользователя; Row-Level Security в PostgreSQL.
- **Подводные камни.** `IgnoreQueryFilters()` отключает защиту целиком. Код,
  который работает вне HTTP-запроса (миграции, фоновые задачи), видит
  *пустоту*, а не все данные (`CurrentUserId == null`). В тестах поэтому
  нужен `IgnoreQueryFilters()` (см. `ApiFactory.WithDbAsync`).
- **Чужое → 404, а не 403.** Ответ не раскрывает, что объект с таким id
  существует.

### 3.8. Ожидаемые ошибки — свои исключения + один обработчик

- **Сделано.** `ExplainerUnavailableException`,
  `TextRecognitionUnavailableException`, `TextRecognitionFailedException`,
  `QuotaExceededException` → `KnownExceptionHandler` → 503/422/429.
  Остальные исключения → 500.
- **Почему.** Глубоко в `DictionaryExplainer` нет `HttpContext`, а
  протаскивать «код ответа» через три слоя неудобно. Обработчик в одном
  месте отвечает за перевод «тип ошибки → HTTP-код».
- **Альтернатива.** Result-тип (`Result<T, Error>` или
  `OneOf<Explanation, Unavailable>`) вместо исключений.
- **Когда лучше.** Когда ошибка — частый и нормальный исход (неверный
  пароль, «уже отвечено»). В проекте так и сделано: `AuthOutcome` и
  `AnswerOutcome` — это result-типы, а не исключения. Правило проекта такое:
  ошибка бизнес-логики — значение, сбой окружения — исключение.

### 3.9. HttpClient через IHttpClientFactory (typed client)

- **Сделано.** `AddHttpClient<DictionaryExplainer>(...)`.
- **Почему.** `new HttpClient()` на каждый запрос исчерпывает сокеты, а
  один статический клиент не замечает смены DNS. Фабрика переиспользует
  обработчики и периодически их обновляет.
- **Альтернатива.** Named clients (`CreateClient("dictionary")`);
  `SocketsHttpHandler` с `PooledConnectionLifetime` в singleton.
- **Ещё можно.** Повторы и circuit breaker через
  `Microsoft.Extensions.Http.Resilience`. В проекте их нет, это задача 6.

### 3.10. Tesseract через командную строку

- **Сделано.** `Process.Start("tesseract", image, "stdout", ...)`, временный
  файл, таймаут, `Kill` при отмене.
- **Почему.** NuGet-обёртки привязаны к версиям нативных библиотек и
  капризны на Linux (по `STATUS.md`). CLI одинаково ставится через apt,
  brew и установщик Windows.
- **Альтернативы.** Обёртка `Tesseract` из NuGet (в процессе, быстрее —
  без запуска процесса на каждый запрос); Windows OCR на клиенте; облачный
  OCR.
- **Цена решения.** Запуск процесса на каждый запрос (сотни миллисекунд),
  временный файл на диске, зависимость от `PATH`.

### 3.11. Время через TimeProvider

- **Сделано.** `TimeProvider` в DI, в тестах — `FakeTimeProvider`.
- **Почему.** Повторение и токены зависят от времени. Тест «через сутки
  элемент снова доступен» без подставных часов пришлось бы ждать сутки.
- **Альтернатива.** Свой `IClock`; `DateTime.UtcNow` напрямую (не
  тестируется).

### 3.12. Аутентификация: короткий JWT + refresh-токен с ротацией

- **Сделано.** Access-токен (JWT) живёт 15 минут, refresh-токен — 30 дней.
  В базе хранится только SHA-256 от refresh-токена. При обновлении старый
  токен отзывается. Если кто-то использует уже отозванный токен,
  отзываются все токены пользователя.
- **Почему.** JWT проверяется без базы, но его нельзя отозвать до
  истечения, поэтому он короткий. Refresh-токен можно отозвать, поэтому
  он длинный. Повторное использование отозванного токена — признак кражи.
- **Альтернативы.** Cookie-сессии (`AddCookie`); ASP.NET Core Identity с
  готовыми эндпоинтами (`MapIdentityApi`); внешний провайдер (Keycloak,
  Auth0).
- **Когда лучше.** Cookie — для браузерного фронтенда на том же домене.
  Identity — когда нужны подтверждение email и сброс пароля «из коробки».

### 3.13. Два вида лимитов

- **Сделано.** Лимит в минуту — встроенный `RateLimiter` в памяти; суточная
  квота — `COUNT` по `Lookup` в базе.
- **Почему.** Минутный лимит защищает от всплеска, и ему не страшно
  сброситься при перезапуске. Суточная квота защищает деньги (LLM), и она
  должна пережить перезапуск.
- **Альтернатива.** Распределённый счётчик в Redis.
- **Когда лучше.** Когда экземпляров приложения несколько: лимит в памяти у
  каждого свой.
- **Слабое место.** Проверка квоты и запись не атомарны: два параллельных
  запроса могут оба пройти на последнем разрешённом разборе. Это вопрос 17.

### 3.14. Адаптивный планировщик — модель полураспада

- **Сделано.** `p(вспомнит через t) = 2^(−t/h)`. `log2 h` линейно зависит
  от √(верных), √(ошибок) и поправки сложности `d`; `d` подбирается по
  истории элемента перебором по сетке с нормальным априорным распределением.
- **Почему.** Модель объяснима на защите, у неё мало параметров, и она
  описана в статье (Settles & Meeder, 2016, half-life regression).
- **Альтернативы.** SM-2 (классика Anki); FSRS (современный Anki, модель
  DSR); логистическая регрессия; нейросеть.
- **Честное ограничение.** Общие веса заданы в конфигурации и не обучаются
  на данных: обучение по `ReviewEvent` — следующий шаг для ВКР (`STATUS.md`).

### 3.15. Отбор «i + 1»

- **Сделано.** Кандидаты ищутся в SQL через `LIKE` по самому длинному слову
  (до 200 штук), точная проверка по словам — в памяти.
- **Почему.** `LIKE '%...%'` не использует индекс, но грубо отсекает
  лишнее. Точная проверка по токенам в SQL неудобна.
- **Альтернатива.** Полнотекстовый поиск SQLite FTS5; отдельная таблица
  «реплика — слово» с индексом.
- **Когда лучше.** Когда реплик сотни тысяч: перебор по `LIKE` станет
  медленным.

### 3.16. Тесты: WebApplicationFactory + реальная SQLite

- **Сделано.** Каждый тест поднимает приложение в памяти со своим файлом
  базы, подставными часами и подставным объяснителем. Запросы идут по
  HTTP через `HttpClient`.
- **Почему.** Проверяется вся цепочка: маршруты, авторизация, фильтры EF,
  сериализация.
- **Альтернативы.** EF InMemory provider (не проверяет SQL и ограничения
  уникальности); SQLite `:memory:`; Testcontainers с PostgreSQL;
  юнит-тесты сервисов с моками `DbContext`.
- **Почему не InMemory.** Он не знает про уникальные индексы и не переводит
  LINQ в SQL. Тест может пройти там, где настоящая база упадёт.
