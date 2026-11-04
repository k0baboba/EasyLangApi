# Результаты прогона

Сгенерировано скриптом `python -m easylang_eval` по ответам сервисов из этого прогона.

- Дата: 2026-11-04 16:43 UTC
- Набор: `phrases.jsonl` (50 фраз), sha256 `97de3f5368c34fd2`
- Модель разбора: `qwen2.5:7b` по адресу `http://127.0.0.1:11434/v1`
- Судья: `llama3.1:8b` по адресу `http://127.0.0.1:11434/v1`
- Словарь: `https://api.dictionaryapi.dev/api/v2/entries/en/`

## Итог

Точность — доля фраз, где судья сопоставил объяснение с правильным смыслом. В квадратных скобках — 95 % доверительный интервал Уилсона. Ошибки сервиса в точность не входят.

| Вариант | Оценено | Ошибок сервиса | Верно | Точность [95 % ДИ] | Неверный смысл | Ни один | Ответ судьи не разобран | Источник «знает» фразу |
|---|---:|---:|---:|---|---:|---:|---:|---:|
| словарь | 46 | 4 | 17 | 37.0 % [25–51] | 27 | 2 | 0 | 39 |
| LLM без контекста | 50 | 0 | 17 | 34.0 % [22–48] | 33 | 0 | 0 | 50 |
| LLM с контекстом | 50 | 0 | 42 | 84.0 % [71–92] | 7 | 1 | 0 | 50 |

## По категориям

| Категория | словарь | LLM без контекста | LLM с контекстом |
|---|---|---|---|
| сленг | 25.0 % [11–47], n=20 | 30.4 % [16–51], n=23 | 73.9 % [54–87], n=23 |
| многозначные слова | 31.6 % [15–54], n=19 | 21.1 % [9–43], n=19 | 94.7 % [75–99], n=19 |
| контроль (верен буквальный смысл) | 85.7 % [49–97], n=7 | 75.0 % [41–93], n=8 | 87.5 % [53–98], n=8 |

В контрольных фразах «неверный смысл» почти всегда означает сленговое прочтение там, где имелось в виду буквальное:

- словарь: 1 из 7
- LLM без контекста: 2 из 8
- LLM с контекстом: 0 из 8

## Парное сравнение

Только фразы, где оценены оба варианта. p — точный тест Макнемара по фразам, где прав только один.

| A против B | Оба верно | Только A | Только B | Оба неверно | p |
|---|---:|---:|---:|---:|---:|
| LLM с контекстом против LLM без контекста | 16 | 26 | 1 | 7 | 4.17e-07 |
| LLM с контекстом против словарь | 16 | 24 | 1 | 5 | 1.55e-06 |
| LLM без контекста против словарь | 6 | 8 | 11 | 21 | 0.648 |

## Ошибки (4)

- dictionary s041: https://api.dictionaryapi.dev/api/v2/entries/en/aggro ответил кодом 502: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-502/","title":"Error 502: Bad gateway","status":502,"detail":"The origin web server returned an invalid or incomplete response to Cloudflare. This typically indicates the origin is overlo
- dictionary s047: https://api.dictionaryapi.dev/api/v2/entries/en/savage ответил кодом 502: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-502/","title":"Error 502: Bad gateway","status":502,"detail":"The origin web server returned an invalid or incomplete response to Cloudflare. This typically indicates the origin is overlo
- dictionary s068: https://api.dictionaryapi.dev/api/v2/entries/en/FOMO ответил кодом 502: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-502/","title":"Error 502: Bad gateway","status":502,"detail":"The origin web server returned an invalid or incomplete response to Cloudflare. This typically indicates the origin is overlo
- dictionary c021: https://api.dictionaryapi.dev/api/v2/entries/en/kite ответил кодом 502: {"type":"https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-502/","title":"Error 502: Bad gateway","status":502,"detail":"The origin web server returned an invalid or incomplete response to Cloudflare. This typically indicates the origin is overlo

Перезапуск с той же папкой `--out` повторит только неудачные запросы.

## Как читать

- Судья — тоже языковая модель. Проверьте вручную случайные строки из `results.csv` (колонка `manual_ok`), прежде чем опираться на числа.
- У словаря оценивается первое значение — то, что приложение показывает первым.
