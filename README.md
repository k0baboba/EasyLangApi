# EasyLangApi

Интеллектуальная система поддержки изучения английского языка. Пользователь смотрит
контент на английском, система разбирает незнакомые фразы **с учётом контекста
произведения** (название, говорящий, соседние реплики), складывает их в личный словарь
и возвращает на повторение.

Подробная постановка — в [SPEC.md](SPEC.md), состояние работы — в [STATUS.md](STATUS.md).

## Быстрый старт

Нужен [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Больше ничего:
база — файл SQLite, создаётся при первом запуске.

Для распознавания текста со скриншотов нужен ещё [Tesseract](https://tesseract-ocr.github.io/tessdoc/Installation.html)
(`sudo apt install tesseract-ocr`, `brew install tesseract`, установщик UB Mannheim для Windows).
Без него приложение работает, а `POST /recognize` отвечает 503 с объяснением.

```bash
dotnet run
```

Приложение слушает `http://localhost:5057`. Файл базы `easylang.db` появится рядом с проектом.

Пройти весь поток можно запросами из [requests.http](requests.http) или через `curl`.
Все эндпоинты, кроме `/auth/*`, требуют токен:

```bash
H=http://localhost:5057
TOKEN=$(curl -s -H "Content-Type: application/json" \
  -d '{"email":"maya@example.com","password":"night-shift-2026"}' $H/auth/register \
  | python3 -c "import sys, json; print(json.load(sys.stdin)['accessToken'])")
AUTH="Authorization: Bearer $TOKEN"

curl -H "$AUTH" -F "file=@sample.srt" -F "title=Night Shift S01E01" $H/episodes
curl -H "$AUTH" $H/episodes/1/lines
curl -H "$AUTH" "$H/lines/3/explain?phrase=beat"
curl -H "$AUTH" $H/vocabulary
curl -H "$AUTH" $H/review/next
curl -H "$AUTH" -H "Content-Type: application/json" -d '{"wasCorrect":true}' $H/review/1
```

Если пользователь уже есть, вместо `/auth/register` — `/auth/login` с тем же телом.

## Docker

```bash
cp .env.example .env        # задайте JWT_SIGNING_KEY
docker compose up --build   # http://localhost:8080
```

Образ содержит Tesseract, база лежит в томе `easylang-data`. С локальной языковой моделью:

```bash
docker compose --profile llm up --build
docker compose exec ollama ollama pull llama3.2
```

и в `.env`: `LLM_API_KEY=ollama`, `LLM_BASE_URL=http://ollama:11434/v1`, `LLM_MODEL=llama3.2`.

Проверка живости — `GET /health`.

## CI

[.github/workflows/ci.yml](.github/workflows/ci.yml): сборка, тесты (с установленным Tesseract,
результаты — артефакт `test-results`), сборка Docker-образа и проверка, что контейнер поднимается
и распознаёт `sample.png`.

## Десктопный клиент

[client/](client/README.md) — приложение для Windows в трее: хоткей, выделение области экрана,
отправка на `/recognize`, показ текста и разбора. Отдельный проект, основное решение от него
не зависит. **На Windows не проверялся.**

```bash
dotnet run --project client/EasyLangClient
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
| `POST` | `/auth/register` | `{"email", "password"}` (пароль от 8 символов) → пара токенов |
| `POST` | `/auth/login` | `{"email", "password"}` → пара токенов |
| `POST` | `/auth/refresh` | `{"refreshToken"}` → новая пара, старый refresh-токен отзывается |
| `POST` | `/auth/logout` | `{"refreshToken"}` — отозвать |
| `GET` | `/auth/me` | Текущий пользователь |
| `GET` | `/health` | Проверка живости, без токена |
| `POST` | `/episodes` | Форма `multipart/form-data`: `file` — файл `.srt`, `title` — название (необязательно). Разбирает на реплики с таймингами |
| `GET` | `/episodes` | Список эпизодов |
| `GET` | `/episodes/{id}/lines` | Реплики эпизода |
| `POST` | `/recognize` | Форма: `image` — PNG/JPEG/BMP/GIF/TIFF/WebP, `title` — название игры (необязательно), `episodeId` — дописать в существующий эпизод. Распознанные абзацы сохраняются как реплики |
| `GET` | `/lines/{id}/explain?phrase=...` | Разбор фразы с контекстом: название эпизода, говорящий, текущая реплика, две до и две после. Результат сохраняется |
| `GET` | `/vocabulary` | Сохранённые элементы и где их видели в последний раз |
| `GET` | `/vocabulary/{id}/examples?singleUnknownOnly=true` | Реплики с элементом и что ещё в них не выучено |
| `GET` | `/review/next` | Следующий элемент на повторение (`204`, если повторять нечего). Каждый вызов — новый показ с `reviewId` |
| `POST` | `/review/{reviewId}` | Тело `{"wasCorrect": true}` — ответ на показ, пересчёт интервала |

Ошибки возвращаются в формате [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457):
`400` — неверный запрос, `404` — нет такой реплики/показа, `409` — на показ уже ответили,
`401` — нет или истёк токен, `422` — Tesseract не смог прочитать изображение,
`429` — превышен лимит, `503` — внешний разборщик или Tesseract недоступен.

## Пользователи и квоты

- Access-токен — JWT (HMAC-SHA256) на 15 минут, refresh-токен — случайная строка на 30 дней,
  в базе хранится только её SHA-256. При обновлении выдаётся новая пара, старый refresh-токен
  отзывается; повторное использование отозванного отзывает все токены пользователя.
- Пароли хешируются `PasswordHasher` из ASP.NET Core Identity (PBKDF2).
- Данные разделены глобальными фильтрами EF Core (`HasQueryFilter`): запрос видит только
  строки текущего пользователя, чужие id отвечают 404.
- Дорогие эндпоинты (`/lines/{id}/explain`, `/recognize`) ограничены по числу запросов в минуту
  на пользователя (`Quotas:ExpensiveRequestsPerMinute`), разборы — ещё и суточной квотой
  (`Quotas:LookupsPerDay`).
- Ключ подписи — `Jwt:SigningKey` (не короче 32 байт). Если не задан, генерируется при запуске:
  удобно для разработки, но access-токены не переживут перезапуск.

## Планировщик повторений

`IReviewScheduler`, выбирается настройкой `Review:Scheduler`:

- `Fixed` (по умолчанию) — лесенка 1, 3, 7, 14, 30, 60, 120 дней; ошибка — снова 1 день.
- `Adaptive` — обучаемая модель забывания из [`srs/`](srs/README.md) (half-life regression).
  Вероятность вспомнить через `t` дней — `2^(-t/h)`, `h = 2^(θ·x)`,
  `x = [1, √верных, √ошибок, последний ответ — ошибка]`. Веса `θ` свои у каждого пользователя:
  они обучаются онлайн-градиентным спуском на его ответах из `ReviewEvent`. Следующий показ —
  когда вероятность вспомнить упадёт до `Review:Adaptive:TargetRetention` (0.8), интервал
  округляется до дней, от 1 до 365. `/review/next` возвращает оценку `forgettingProbability`.
  Как реализация на C# проверена против Python-версии — в [srs/REPORT.md](srs/REPORT.md).

`/review/next` показывает фразу в реплике, где незнакомо **ровно одно** — повторяемое
(принцип «i + 1»). Незнакомыми считаются элементы словаря с интервалом меньше
`Review:KnownAfterDays` (21 день). Если такой реплики нет, показывается та, где фразу смотрели,
с `singleUnknown: false` и списком `otherUnknown`.

## Распознавание текста

`ITextRecognizer` → `TesseractRecognizer` запускает командную строку `tesseract`.
Распознанный текст попадает в тот же поток, что и субтитры: оба источника реализуют
`ITextSource` (`SrtTextSource`, `ImageTextSource`) и сохраняются через `EpisodeImporter`.
Поэтому `/lines/{id}/explain` для реплики со скриншота получает тот же контекст:
название (`title`), соседние абзацы и реплики предыдущих скриншотов того же эпизода.

## Модель данных

Фраза — единица показа, слово (выражение) — единица учёта.

- `User`, `RefreshToken` — пользователи и их refresh-токены (хешами).
- `Episode`, `Line` — источник текста и реплики с таймингами.
- `Lookup` — наблюдение: какую фразу посмотрели, в какой реплике, что ответил разборщик.
- `KnowledgeItem` — элемент учёта: нормализованная фраза, интервал, срок повторения.
- `ReviewEvent` — сырая история показов и ответов. Ничего не удаляется и не агрегируется.

## Разбор фраз

Интерфейс `IPhraseExplainer`, две реализации:

- `DictionaryExplainer` — [dictionaryapi.dev](https://dictionaryapi.dev), бесплатно, без ключа.
  Работает по умолчанию. Контекст использует только для порядка значений.
- `LlmExplainer` — любой API, совместимый с OpenAI Chat Completions. Включается, если задан `Llm:ApiKey`.
  Получает промпт с контекстом (`PromptBuilder`) и просит ответ в JSON: перевод, смысл в сцене,
  часть речи, заметка о регистре/сленге, пример. Если модель ответила не JSON,
  текст ответа сохраняется как есть (`LlmResponseParser`).

Примеры настройки языковой модели:

```bash
# OpenAI
dotnet user-secrets set "Llm:ApiKey" "sk-..."

# Локальный Ollama (ключ не проверяется, но должен быть непустым — это включатель)
ollama pull llama3.2
Llm__ApiKey=ollama Llm__BaseUrl=http://localhost:11434/v1 Llm__Model=llama3.2 dotnet run
```

Если сервис не понимает поле `response_format`, выключите его: `Llm:UseJsonMode=false`.

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
| `Llm:UseJsonMode` | `true` | Передавать `response_format: json_object` |
| `Jwt:SigningKey` | пусто (случайный) | Секрет подписи JWT, ≥ 32 байт |
| `Jwt:AccessTokenMinutes` | `15` | Срок жизни access-токена |
| `Jwt:RefreshTokenDays` | `30` | Срок жизни refresh-токена |
| `Quotas:ExpensiveRequestsPerMinute` | `30` | Лимит разборов и распознаваний в минуту |
| `Quotas:LookupsPerDay` | `500` | Суточная квота разборов |
| `Review:Scheduler` | `Fixed` | `Fixed` или `Adaptive` |
| `Review:FixedIntervals` | `1,3,7,14,30,60,120` | Ступеньки фиксированного планировщика |
| `Review:KnownAfterDays` | `21` | С какого интервала элемент считается выученным |
| `Review:Adaptive:TargetRetention` | `0.8` | Целевая вероятность вспомнить |
| `Review:Adaptive:LearningRate`, `L2` | `0.05`, `0.0001` | Обучение модели |
| `Review:Adaptive:InitialWeights` | пусто (нули) | Начальные веса `θ` |
| `Review:Adaptive:RoundToWholeDays` | `true` | Округлять интервал до дней |
| `Ocr:TesseractPath` | `tesseract` | Путь к Tesseract (по умолчанию из PATH) |
| `Ocr:Language` | `eng` | Языки распознавания, например `eng+rus` |
| `Ocr:PageSegmentationMode` | пусто | `--psm` Tesseract; для одного блока диалога попробуйте `6` |

Ключи не коммитьте: для локальной работы удобнее `dotnet user-secrets set "Llm:ApiKey" "..."`.

## Структура

```
Data/          EF Core: контекст, сущности, миграции
Episodes/      загрузка субтитров, разбор SRT, ITextSource
Explaining/    IPhraseExplainer, словарь, языковая модель, сборщик промпта
Lookups/       сценарий «разобрать фразу»
Recognition/   распознавание текста (Tesseract), POST /recognize
Vocabulary/    нормализация фраз, список словаря
Review/        выдача на повторение, планировщики, отбор фраз «i + 1»
Auth/          регистрация, вход, JWT и refresh-токены
Quotas/        лимиты запросов и суточная квота
Infrastructure/ обработка ожидаемых ошибок
tests/         xUnit
client/        клиент для Windows (отдельный проект)
```

Миграции создаются локальным инструментом: `dotnet tool restore`, затем
`dotnet ef migrations add <Имя> -o Data/Migrations`.
