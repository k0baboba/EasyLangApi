# Результаты прогона

Сгенерировано скриптом `python -m easylang_eval` по ответам сервисов из этого прогона.

- Дата: 2026-11-04 16:43 UTC
- Набор: `phrases.jsonl` (150 фраз), sha256 `97de3f5368c34fd2`
- Модель разбора: `qwen2.5:7b` по адресу `http://127.0.0.1:11434/v1`
- Судья: `llama3.1:8b` по адресу `http://127.0.0.1:11434/v1`
- Словарь: `https://api.dictionaryapi.dev/api/v2/entries/en/`

## Итог

Точность — доля фраз, где судья сопоставил объяснение с правильным смыслом. В квадратных скобках — 95 % доверительный интервал Уилсона. Ошибки сервиса в точность не входят.

| Вариант | Оценено | Ошибок сервиса | Верно | Точность [95 % ДИ] | Неверный смысл | Ни один | Ответ судьи не разобран | Источник «знает» фразу |
|---|---:|---:|---:|---|---:|---:|---:|---:|
| словарь | 146 | 4 | 63 | 43.2 % [35–51] | 78 | 5 | 0 | 120 |
| LLM без контекста | 150 | 0 | 67 | 44.7 % [37–53] | 79 | 4 | 0 | 150 |
| LLM с контекстом | 150 | 0 | 133 | 88.7 % [83–93] | 15 | 2 | 0 | 150 |

## По категориям

| Категория | словарь | LLM без контекста | LLM с контекстом |
|---|---|---|---|
| сленг | 23.9 % [15–35], n=67 | 51.4 % [40–63], n=70 | 82.9 % [72–90], n=70 |
| многозначные слова | 43.6 % [31–57], n=55 | 29.1 % [19–42], n=55 | 94.5 % [85–98], n=55 |
| контроль (верен буквальный смысл) | 95.8 % [80–99], n=24 | 60.0 % [41–77], n=25 | 92.0 % [75–98], n=25 |

В контрольных фразах «неверный смысл» почти всегда означает сленговое прочтение там, где имелось в виду буквальное:

- словарь: 1 из 24
- LLM без контекста: 9 из 25
- LLM с контекстом: 1 из 25

## Парное сравнение

Только фразы, где оценены оба варианта. p — точный тест Макнемара по фразам, где прав только один.

| A против B | Оба верно | Только A | Только B | Оба неверно | p |
|---|---:|---:|---:|---:|---:|
| LLM с контекстом против LLM без контекста | 65 | 68 | 2 | 15 | 4.21e-18 |
| LLM с контекстом против словарь | 59 | 72 | 4 | 11 | 3.59e-17 |
| LLM без контекста против словарь | 30 | 34 | 33 | 49 | 1 |

## Ошибки (4)

- dictionary s041: https://api.dictionaryapi.dev/api/v2/entries/en/aggro ответил кодом 502: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-502/","title":"Error 502: Bad gateway","status":502,"detail":"The origin web server returned an invalid or incomplete response to Cloudflare. This typically indicates the origin is overlo
- dictionary s047: https://api.dictionaryapi.dev/api/v2/entries/en/savage ответил кодом 502: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-502/","title":"Error 502: Bad gateway","status":502,"detail":"The origin web server returned an invalid or incomplete response to Cloudflare. This typically indicates the origin is overlo
- dictionary s068: https://api.dictionaryapi.dev/api/v2/entries/en/FOMO ответил кодом 502: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-502/","title":"Error 502: Bad gateway","status":502,"detail":"The origin web server returned an invalid or incomplete response to Cloudflare. This typically indicates the origin is overlo
- dictionary c021: https://api.dictionaryapi.dev/api/v2/entries/en/kite ответил кодом 502: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-502/","title":"Error 502: Bad gateway","status":502,"detail":"The origin web server returned an invalid or incomplete response to Cloudflare. This typically indicates the origin is overlo

Перезапуск с той же папкой `--out` повторит только неудачные запросы.

## Как читать

- Судья — тоже языковая модель. Проверьте вручную случайные строки из `results.csv` (колонка `manual_ok`), прежде чем опираться на числа.
- У словаря оценивается первое значение — то, что приложение показывает первым.
