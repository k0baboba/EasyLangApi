# 4. Архитектура

> Черновик. Описывает **целевую** архитектуру по `SPEC.md`. На момент
> написания в репозитории есть только пустой проект ASP.NET Core
> (`Program.cs`) — **[факт]**, проверено по содержимому репозитория.
> Всё, что ниже, — проект, а не описание готового кода. Названия классов,
> кроме интерфейсов из `SPEC.md`, условные — **[предположение]**.

## 4.1. Контекст системы

```mermaid
flowchart LR
    U(["Пользователь"])
    C["Десктопный клиент<br/>(V7, Windows)"]
    H["HTTP-клиент<br/>requests.http / curl"]
    S["EasyLangApi<br/>ASP.NET Core"]
    DB[("SQLite")]
    D["dictionaryapi.dev"]
    L["OpenAI-совместимый API<br/>облако или локальный Ollama"]
    T["Tesseract<br/>(локально на сервере)"]

    U --> C
    U --> H
    C -- "скриншот области" --> S
    H -- "srt, запросы" --> S
    S --> DB
    S -- "по умолчанию" --> D
    S -. "если задан ключ" .-> L
    S -. "если установлен" .-> T
```

Сплошные стрелки — обязательные зависимости, пунктир — необязательные: без
них система работает в урезанном режиме (NFR-04).

## 4.2. Логические слои

[предположение] Для учебного монолита достаточно трёх слоёв внутри одного
проекта. Отдельные проекты (Clean Architecture) для прототипа избыточны.

```mermaid
flowchart TB
    subgraph API["Слой API: minimal API эндпоинты"]
        E1["/episodes"]
        E2["/lines/{id}/explain"]
        E3["/vocabulary"]
        E4["/review/next, /review/{id}"]
        E5["/recognize"]
    end

    subgraph Domain["Слой предметной логики"]
        SP["Разбор .srt"]
        CB["Сборщик контекста<br/>PhraseContext"]
        EX["IPhraseExplainer"]
        TS["ITextSource"]
        TR["ITextRecognizer"]
        RS["IReviewScheduler"]
        N["Нормализация фразы"]
    end

    subgraph Infra["Слой инфраструктуры"]
        EF["EF Core DbContext"]
        DE["DictionaryExplainer"]
        LE["LlmExplainer"]
        TE["Tesseract-распознаватель"]
        FS["Фиксированный планировщик"]
        AS["Адаптивный планировщик"]
    end

    E1 --> SP --> EF
    E2 --> CB --> EX
    E2 --> N --> EF
    E5 --> TR
    TR --> TS
    SP --> TS
    E3 --> EF
    E4 --> RS --> EF
    EX -.реализация.- DE
    EX -.реализация.- LE
    TR -.реализация.- TE
    RS -.реализация.- FS
    RS -.реализация.- AS
```

Ключевой приём — **интерфейс и несколько реализаций, выбор в
конфигурации**. Он повторяется трижды (`IPhraseExplainer`,
`ITextRecognizer`, `IReviewScheduler`) и опирается на встроенный в ASP.NET
Core контейнер зависимостей. Для ВКР это удобная точка: одна и та же
архитектурная идея решает и плавную деградацию (нет ключа → словарь), и
эксперимент (сравнить два планировщика на одних данных).

Документация: [Dependency injection in ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection).

## 4.3. Модель данных

[факт, SPEC.md] Фраза — единица показа, слово — единица учёта. Наблюдение и
элемент знания хранятся отдельно.

```mermaid
erDiagram
    Episode ||--o{ Line : "содержит"
    Line ||--o{ Lookup : "в ней посмотрели"
    KnowledgeItem ||--o{ Lookup : "наблюдения элемента"
    KnowledgeItem ||--o{ ReviewEvent : "история ответов"

    Episode {
        int id PK
        string title
        string source_file
        datetime created_at
    }
    Line {
        int id PK
        int episode_id FK
        int index
        int start_ms
        int end_ms
        string text
    }
    Lookup {
        int id PK
        int line_id FK
        string phrase
        string explanation_json
        datetime created_at
    }
    KnowledgeItem {
        int id PK
        string phrase_normalized
        datetime first_seen_at
        double interval_days
        datetime due_at
        double ease
    }
    ReviewEvent {
        int id PK
        int knowledge_item_id FK
        datetime shown_at
        datetime answered_at
        bool was_correct
    }
```

[предположение] В `SPEC.md` связь `Lookup → KnowledgeItem` явно не указана:
у `Lookup` нет поля `knowledge_item_id`. Связать их можно двумя способами:

1. добавить внешний ключ `Lookup.knowledge_item_id`;
2. связывать по значению: нормализовать `Lookup.phrase` и искать
   `KnowledgeItem.phrase_normalized`.

На диаграмме нарисован первый вариант. Это решение владельца проекта, его
нужно принять и отразить в `SPEC.md`. Вопрос для обдумывания: что
произойдёт со связью во втором варианте, если позже поменяется правило
нормализации?

[предположение] Индексы, которые понадобятся почти наверняка:
`Line(episode_id, index)` для выборки соседних реплик,
`KnowledgeItem(phrase_normalized)` уникальный,
`KnowledgeItem(due_at)` для `GET /review/next`,
`ReviewEvent(knowledge_item_id, shown_at)` для адаптивного планировщика.

## 4.4. Последовательности

### Загрузка субтитров

```mermaid
sequenceDiagram
    actor U as Пользователь
    participant API as POST /episodes
    participant P as Разбор .srt
    participant DB as SQLite

    U->>API: файл .srt
    API->>P: поток файла
    P-->>API: список реплик (index, start, end, text)
    API->>DB: Episode + Lines
    DB-->>API: id эпизода
    API-->>U: 201 Created, id, число реплик
```

### Разбор фразы с контекстом

```mermaid
sequenceDiagram
    actor U as Пользователь
    participant API as GET /lines/{id}/explain
    participant DB as SQLite
    participant CB as Сборщик контекста
    participant EX as IPhraseExplainer
    participant EXT as Словарь или LLM

    U->>API: id реплики, phrase
    API->>DB: реплика, эпизод, соседи ±2
    DB-->>API: данные
    API->>CB: что пришло
    CB-->>API: PhraseContext (Title, Before, After, ...)
    API->>EX: ExplainAsync(phrase, context)
    EX->>EXT: HTTP-запрос
    alt внешний сервис ответил
        EXT-->>EX: ответ
        EX-->>API: Explanation
        API->>DB: Lookup + KnowledgeItem (создать или найти)
        API-->>U: 200, объяснение
    else ошибка или таймаут
        EXT-->>EX: ошибка
        EX-->>API: исключение
        API-->>U: понятная ошибка (например, 502 или 503)
    end
```

[предположение] Выбор кода ответа при отказе внешнего сервиса — хорошая
тема для разбора обработки исключений: чем ошибка «сервис словаря
недоступен» отличается от «слово не найдено» и почему у них должны быть
разные коды. См. [Handle errors in ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/error-handling)
и [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457).

### Повторение

```mermaid
sequenceDiagram
    actor U as Пользователь
    participant API as Review API
    participant RS as IReviewScheduler
    participant DB as SQLite

    U->>API: GET /review/next
    API->>DB: ближайший KnowledgeItem по due_at
    API->>DB: ReviewEvent (shown_at)
    API-->>U: элемент + пример реплики из Lookup
    U->>API: POST /review/{id} {remembered}
    API->>DB: ReviewEvent (answered_at, was_correct)
    API->>RS: история элемента
    RS-->>API: новый interval_days, due_at, ease
    API->>DB: обновить KnowledgeItem
    API-->>U: 200, следующая дата
```

[предположение] Порядок «сначала записать событие, потом пересчитать» важен:
даже если пересчёт упадёт, история ответов не потеряется. Это напрямую
следует из требования FR-16 о полноте `ReviewEvent`.

### Распознавание с экрана (V2 + V7)

```mermaid
sequenceDiagram
    actor U as Пользователь
    participant C as Клиент Windows
    participant API as POST /recognize
    participant TR as ITextRecognizer
    participant TS as ITextSource

    U->>C: горячая клавиша, выбор области
    C->>API: PNG области
    API->>TR: изображение
    alt Tesseract установлен
        TR-->>API: текст
        API->>TS: текст как источник реплики
        API-->>C: текст и id для разбора
        C-->>U: оверлей
    else не установлен
        API-->>C: понятная ошибка
    end
```

Где выполнять распознавание — на клиенте или на сервере — разобрано в
разделе 5.

## 4.5. Развёртывание

```mermaid
flowchart LR
    subgraph PC["ПК пользователя (Windows)"]
        CL["Клиент V7"]
    end
    subgraph Host["Хост: локально или Docker (V6)"]
        APP["EasyLangApi<br/>+ Tesseract"]
        F[("easylang.db")]
        OL["Ollama (опционально)"]
    end
    EXT["dictionaryapi.dev"]
    CL -- "HTTP" --> APP
    APP --> F
    APP -.-> OL
    APP --> EXT
```

[факт, SPEC.md] Публичного деплоя нет; `docker-compose` поднимает
приложение и опциональный Ollama.
