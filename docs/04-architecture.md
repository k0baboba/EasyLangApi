# 4. Архитектура

> Черновик. Раздел описывает **код ветки `prototype`** — версии V1–V7 из `SPEC.md`
> и обучаемый планировщик, перенесённый из `srs/`. Первая версия раздела писалась,
> когда в репозитории был только пустой проект ASP.NET Core, и описывала целевую
> архитектуру с условными именами классов. Теперь имена настоящие, пути к файлам
> даны от корня репозитория. Пометки: **[код]** — так сделано в коде;
> **[факт]** — следует из `SPEC.md` или источника; **[предположение]** — оценка автора.

## 4.1. Контекст системы

```mermaid
flowchart LR
    U(["Пользователь"])
    C["Десктопный клиент<br/>client/ (Windows)"]
    H["HTTP-клиент<br/>requests.http / curl"]
    S["EasyLangApi<br/>ASP.NET Core"]
    DB[("SQLite<br/>easylang.db")]
    D["dictionaryapi.dev"]
    L["OpenAI-совместимый API<br/>облако или локальный Ollama"]
    T["tesseract<br/>(процесс на сервере)"]

    U --> C
    U --> H
    C -- "PNG области + JWT" --> S
    H -- "srt, запросы + JWT" --> S
    S --> DB
    S -- "если нет Llm:ApiKey" --> D
    S -. "если задан Llm:ApiKey" .-> L
    S -. "если установлен" .-> T
```

Сплошные стрелки — обязательные зависимости, пунктир — необязательные: без них
система работает в урезанном режиме (NFR-04).

[код] Без Tesseract приложение стартует, а `POST /recognize` отвечает 503 с объяснением
(`Recognition/TesseractRecognizer.cs`). Без ключа LLM работает словарь
(`Explaining/ExplainingServiceCollectionExtensions.cs`). Если же внешний сервис **выбран, но
недоступен** (словарь не отвечает, у LLM неверный ключ), запрос отвечает 503: автоматического
перехода с LLM на словарь нет.

## 4.2. Устройство кода

[код] Один проект `EasyLangApi.csproj` в корне, папки по функциям, а не по слоям. Внутри каждой
функции три уровня: эндпоинт (minimal API) → сервис со сценарием → `AppDbContext` и внешние
сервисы за интерфейсами. Отдельные проекты в духе Clean Architecture не заводились.

| Папка | Что внутри |
|---|---|
| `Data/` | `AppDbContext`, сущности, миграции EF Core, глобальные фильтры по владельцу |
| `Episodes/` | `POST /episodes`, разбор SRT (`SrtParser`), `ITextSource`, `EpisodeImporter`, имя говорящего (`SpeakerParser`) |
| `Recognition/` | `POST /recognize`, `ITextRecognizer` → `TesseractRecognizer`, `ImageTextSource` |
| `Explaining/` | `IPhraseExplainer` → `DictionaryExplainer`, `LlmExplainer`; `PromptBuilder`, `LlmResponseParser` |
| `Lookups/` | `GET /lines/{id}/explain`, `LookupService`: контекст ±2 реплики, разбор, `Lookup` + `KnowledgeItem` |
| `Vocabulary/` | `GET /vocabulary`, `GET /vocabulary/{id}/examples`, нормализация фраз (`PhraseNormalizer`) |
| `Review/` | `GET /review/next`, `POST /review/{id}`, `ReviewService`, `IReviewScheduler` → `FixedIntervalScheduler`, `AdaptiveScheduler` (`HalfLifeRegression`), отбор «i + 1» (`SingleUnknownSelector`, `ExampleLineFinder`) |
| `Auth/` | `/auth/*`, `AuthService`, `TokenService` (JWT + refresh), `ICurrentUser` |
| `Quotas/` | лимит запросов в минуту и суточная квота разборов |
| `Infrastructure/` | `KnownExceptionHandler`: ожидаемые исключения → ProblemDetails с кодом 503/422/429 |
| `client/` | клиент для Windows, отдельный проект, в сборку приложения не входит |
| `srs/` | эксперимент с планировщиками на Python и мост к C#-планировщику, в сборку приложения не входит |

```mermaid
flowchart TB
    subgraph API["Эндпоинты (minimal API)"]
        E1["POST /episodes"]
        E5["POST /recognize"]
        E2["GET /lines/{id}/explain"]
        E3["GET /vocabulary"]
        E4["GET /review/next<br/>POST /review/{id}"]
        E6["/auth/*"]
    end

    subgraph Services["Сценарии"]
        SRT["SrtTextSource"]
        IMG["ImageTextSource"]
        IMP["EpisodeImporter"]
        LS["LookupService"]
        RV["ReviewService"]
        EXF["ExampleLineFinder"]
        AU["AuthService"]
    end

    subgraph Ports["Интерфейсы и реализации"]
        EX["IPhraseExplainer"]
        DE["DictionaryExplainer"]
        LE["LlmExplainer"]
        TR["ITextRecognizer"]
        TE["TesseractRecognizer"]
        RS["IReviewScheduler"]
        FS["FixedIntervalScheduler"]
        AS["AdaptiveScheduler"]
    end

    DB[("AppDbContext<br/>SQLite")]

    E1 --> SRT --> IMP
    E5 --> IMG --> TR
    IMG --> IMP
    IMP --> DB
    E2 --> LS --> EX
    LS --> DB
    E3 --> DB
    E3 --> EXF
    E4 --> RV --> RS
    RV --> EXF --> DB
    RV --> DB
    E6 --> AU --> DB
    EX -.- DE
    EX -.- LE
    TR -.- TE
    RS -.- FS
    RS -.- AS
```

Ключевой приём — **интерфейс и несколько реализаций, выбор в конфигурации**. [код] Он
повторяется трижды и опирается на встроенный контейнер зависимостей ASP.NET Core:

| Интерфейс | Реализации | Как выбирается |
|---|---|---|
| `IPhraseExplainer` | `DictionaryExplainer`, `LlmExplainer` | непустой `Llm:ApiKey` → LLM, иначе словарь |
| `ITextRecognizer` | `TesseractRecognizer` | одна реализация; в тестах подменяется |
| `IReviewScheduler` | `FixedIntervalScheduler`, `AdaptiveScheduler` | `Review:Scheduler` = `Fixed` (по умолчанию) или `Adaptive` |
| `ITextSource` | `SrtTextSource`, `ImageTextSource` | по эндпоинту: субтитры или скриншот |

Документация: [Dependency injection in ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection).

## 4.3. Модель данных

[факт, SPEC.md] Фраза — единица показа, слово — единица учёта. Наблюдение и элемент
знания хранятся отдельно. [код] `Data/Entities.cs`, миграции `Data/Migrations/`.

```mermaid
erDiagram
    User ||--o{ Episode : "владеет"
    User ||--o{ KnowledgeItem : "владеет"
    User ||--o{ RefreshToken : "сессии"
    Episode ||--o{ Line : "содержит"
    Line ||--o{ Lookup : "в ней посмотрели"
    KnowledgeItem ||--o{ Lookup : "наблюдения элемента"
    KnowledgeItem ||--o{ ReviewEvent : "история показов"

    User {
        int Id PK
        string Email "уникальный, в нижнем регистре"
        string PasswordHash "PBKDF2"
        datetime CreatedAt
    }
    RefreshToken {
        int Id PK
        int UserId FK
        string TokenHash "SHA-256, уникальный"
        datetime CreatedAt
        datetime ExpiresAt
        datetime RevokedAt "null — действует"
    }
    Episode {
        int Id PK
        int UserId FK
        string Title
        string SourceFile
        datetime CreatedAt
    }
    Line {
        int Id PK
        int EpisodeId FK
        int Index
        int StartMs
        int EndMs
        string Text
    }
    Lookup {
        int Id PK
        int LineId FK
        int KnowledgeItemId FK
        string Phrase
        string ExplanationJson
        datetime CreatedAt
    }
    KnowledgeItem {
        int Id PK
        int UserId FK
        string PhraseNormalized
        datetime FirstSeenAt
        double IntervalDays
        datetime DueAt
        double Ease
    }
    ReviewEvent {
        int Id PK
        int KnowledgeItemId FK
        datetime ShownAt
        datetime AnsweredAt "null — показ без ответа"
        bool WasCorrect "null — показ без ответа"
    }
```

Отличия от схемы в `SPEC.md` [код]:

- **`Lookup.KnowledgeItemId`** — выбран первый из двух вариантов, которые обсуждала прежняя
  версия раздела: явный внешний ключ, а не связь по нормализованной строке. Если правило
  нормализации поменяется, старые наблюдения останутся привязаны к своим элементам.
- **`UserId`** у `Episode` и `KnowledgeItem` (V5). У `Line`, `Lookup`, `ReviewEvent` владельца
  нет: он определяется через связь. Разделение данных сделано глобальными фильтрами EF Core
  (`HasQueryFilter`): любой запрос видит только строки текущего пользователя.
- **`User`, `RefreshToken`** — таблицы для входа (V5).
- `ReviewEvent.AnsweredAt` и `WasCorrect` допускают null: `GET /review/next` записывает показ,
  `POST /review/{id}` дописывает в ту же строку ответ. Показы без ответа остаются в истории.
- Время хранится в UTC; SQLite не хранит `DateTimeKind`, поэтому при чтении значения помечаются как UTC.

Индексы [код] (прежний раздел предлагал первые четыре):

| Индекс | Зачем |
|---|---|
| `Line(EpisodeId, Index)`, уникальный | соседние реплики для контекста |
| `KnowledgeItem(UserId, PhraseNormalized)`, уникальный | одна фраза — один элемент у пользователя |
| `KnowledgeItem(UserId, DueAt)` | `GET /review/next`: самый просроченный элемент |
| `ReviewEvent(KnowledgeItemId)` | история элемента. Составного `(KnowledgeItemId, ShownAt)` нет: истории короткие |
| `Users(Email)`, `RefreshTokens(TokenHash)`, уникальные | вход и обновление токена |
| `Episode(UserId)`, `Lookup(LineId)`, `Lookup(KnowledgeItemId)`, `RefreshToken(UserId)` | внешние ключи |

`ReviewEvent → KnowledgeItem` — `DeleteBehavior.Restrict`: история не удаляется каскадом.

## 4.4. Последовательности

### Загрузка субтитров

```mermaid
sequenceDiagram
    actor U as Пользователь
    participant API as POST /episodes
    participant P as SrtTextSource / SrtParser
    participant I as EpisodeImporter
    participant DB as SQLite

    U->>API: multipart: file (.srt), title
    API->>P: поток файла
    P-->>API: реплики (start, end, text)
    alt реплик нет
        API-->>U: 400 ProblemDetails
    else есть
        API->>I: title, имя файла, реплики
        I->>DB: Episode (UserId) + Lines, один SaveChanges
        API-->>U: 201 Created, id, число реплик
    end
```

[код] Битые блоки SRT пропускаются, теги `<i>` и `{\an8}` вырезаются, строки реплики
склеиваются через пробел, `Index` — порядковый номер в файле, а не номер блока.

### Разбор фразы с контекстом

```mermaid
sequenceDiagram
    actor U as Пользователь
    participant API as GET /lines/{id}/explain
    participant LS as LookupService
    participant DB as SQLite
    participant EX as IPhraseExplainer
    participant EXT as Словарь или LLM

    U->>API: id реплики, phrase
    API->>API: проверка фразы (400)
    API->>LS: ExplainAsync
    LS->>DB: реплика и эпизод (чужая → нет)
    alt реплики нет
        API-->>U: 404
    else
        LS->>DB: разборов сегодня (суточная квота)
        LS->>DB: соседи ±2 по (EpisodeId, Index)
        LS->>EX: ExplainAsync(phrase, PhraseContext)
        EX->>EXT: HTTP
        alt ответ получен
            EXT-->>EX: данные
            EX-->>LS: Explanation
            LS->>DB: Lookup + KnowledgeItem (найти или создать)
            API-->>U: 200: контекст и объяснение
        else сервис недоступен
            EX-->>LS: ExplainerUnavailableException
            API-->>U: 503 ProblemDetails, ничего не сохранено
        end
    end
```

[код] `PhraseContext` — `Title`, `Speaker` (из префикса `MAYA: ...`), `Before`, `CurrentLine`,
`After`; поле `CurrentLine` добавлено к перечисленным в `SPEC.md`. Коды ответов различают
причины, как и предлагала прежняя версия раздела: 400 — фраза без слов, 404 — нет такой реплики
(или она чужая), 429 — лимит или квота, 503 — внешний сервис недоступен. «Слово не найдено»
(404 словаря) — не отказ: ответ 200 с `found: false`. Для выражения из нескольких слов так же
трактуется и 5xx словаря: показываются значения отдельных слов.

### Повторение

```mermaid
sequenceDiagram
    actor U as Пользователь
    participant API as Review API
    participant RV as ReviewService
    participant RS as IReviewScheduler
    participant DB as SQLite

    U->>API: GET /review/next
    API->>RV: NextAsync
    RV->>DB: самый просроченный KnowledgeItem
    RV->>DB: реплики с фразой, невыученные элементы
    RV->>RS: ForgettingProbability (история, ответы пользователя)
    RV->>DB: ReviewEvent (ShownAt)
    API-->>U: reviewId, фраза, реплика «i + 1», forgettingProbability
    U->>API: POST /review/{reviewId} {wasCorrect}
    API->>RV: AnswerAsync
    RV->>DB: показ, история элемента, все ответы пользователя
    RV->>RS: Schedule(элемент, история, ответы)
    RS-->>RV: IntervalDays, DueAt
    RV->>DB: AnsweredAt и WasCorrect + KnowledgeItem, один SaveChanges
    API-->>U: 200: интервал и дата
```

[код] Расхождение с рекомендацией прежней версии раздела («сначала записать событие, потом
пересчитать»): ответ и новый интервал сохраняются одним `SaveChanges`. Если планировщик
выбросит исключение, ответ не сохранится. Пока планировщики не обращаются к внешним сервисам,
это маловероятно, но полноте `ReviewEvent` (FR-16) двухшаговая запись соответствовала бы лучше.

[код] Адаптивный планировщик (`Review/AdaptiveScheduler.cs`, `Review/HalfLifeRegression.cs`) —
half-life regression из `srs/`. Веса модели нигде не хранятся: при каждом решении они заново
обучаются по всем ответам пользователя из `ReviewEvent`. Совпадение решений C# и Python
проверено в `srs/REPORT.md`, раздел «Проверка C#-планировщика».

### Распознавание с экрана (V2 + V7)

```mermaid
sequenceDiagram
    actor U as Пользователь
    participant C as Клиент Windows
    participant API as POST /recognize
    participant IS as ImageTextSource
    participant TR as TesseractRecognizer
    participant I as EpisodeImporter

    U->>C: хоткей, выбор области на стоп-кадре
    C->>API: PNG, title (заголовок окна), episodeId
    API->>API: сигнатура файла (400, если не картинка)
    API->>IS: поток изображения
    IS->>TR: RecognizeAsync
    alt tesseract есть
        TR-->>IS: текст, абзацы через пустую строку
        IS-->>API: абзац = реплика
        API->>I: новый эпизод или дописать в episodeId
        API-->>C: текст и id реплик
        C-->>U: окно с кликабельными словами
        U->>C: слово → «Разобрать»
        C->>API: GET /lines/{id}/explain
    else tesseract не найден
        API-->>C: 503 «Tesseract не найден»
    end
```

[код] Клиент дописывает скриншоты одного окна в один эпизод (`episodeId`), поэтому предыдущие
окна диалога попадают в контекст разбора как соседние реплики. Изображение сервер сохраняет
только во временный файл на время работы tesseract и затем удаляет.

Где выполнять распознавание — на клиенте или на сервере — разобрано в разделе 5.

## 4.5. Развёртывание

```mermaid
flowchart LR
    subgraph PC["ПК пользователя (Windows)"]
        CL["client/EasyLangClient<br/>settings.json: serverUrl"]
    end
    subgraph Host["Хост: dotnet run или docker compose"]
        APP["EasyLangApi + tesseract<br/>порт 5057 / 8080"]
        F[("том /data<br/>easylang.db")]
        OL["Ollama<br/>профиль llm"]
    end
    EXT["dictionaryapi.dev"]
    CL -- "HTTP + JWT" --> APP
    APP --> F
    APP -.-> OL
    APP --> EXT
```

[код] `Dockerfile`: сборка в образе SDK, запуск в образе aspnet с `tesseract-ocr`, без root,
база в томе `/data`. `docker-compose.yml`: приложение и Ollama в профиле `llm`; секреты — из `.env`.
CI (`.github/workflows/ci.yml`): сборка и тесты с Tesseract, сборка образа и проверка, что контейнер
распознаёт `sample.png`, отдельная необязательная сборка клиента на Windows.

[факт, SPEC.md] Публичного деплоя нет.
