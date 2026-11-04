# srs — эксперимент с планировщиками повторений

Отдельный проект на Python (только стандартная библиотека, 3.11+; числа в отчёте получены на 3.13).
Основное приложение на ASP.NET Core от него не зависит: `EasyLangApi.csproj` исключает `srs/**`
из сборки. Обратная связь есть: адаптивный планировщик приложения — перенос модели отсюда,
и мост `bridge/` проверяет, что C#-версия на тех же условиях принимает те же решения.

Сравниваются планировщик с фиксированными интервалами и адаптивный планировщик (half-life regression),
который оценивает вероятность забывания по истории ответов. Проверка идёт на синтетическом ученике.
Результаты, вывод и ограничения — в [`REPORT.md`](REPORT.md).

```
srs_sim/events.py       ReviewEvent и признаки истории ответов
srs_sim/schedulers.py   FixedIntervalScheduler, AdaptiveScheduler
srs_sim/learner.py      синтетический ученик (модель памяти по мотивам DSR/FSRS)
srs_sim/simulation.py   прогон планировщика на ученике, метрики
srs_sim/experiment.py   сценарии, seed'ы, агрегирование
srs_sim/report.py       таблицы и графики mermaid для REPORT.md
srs_sim/golden.py       эталон решений для C#-теста (tests/EasyLangApi.Tests/Assets/hlr-golden.json)
srs_sim/csharp_check.py прогон C#-планировщика приложения в этой же симуляции, сравнение с Python
bridge/                 консольный мост: вызывает EasyLangApi.Review.AdaptiveScheduler по строкам JSON
tests/                  unittest
```

```bash
cd srs
python3 -m unittest discover -s tests -t .                  # быстрые тесты, <1 с
python3 -m srs_sim                                          # полный эксперимент, ~30 с, обновляет REPORT.md
SRS_FULL=1 python3 -m unittest tests.test_reproducibility   # REPORT.md совпадает с новым прогоном

# нужен .NET 10 SDK:
python3 -m srs_sim.csharp_check                             # C#-планировщик на тех же условиях, ~2–5 мин, обновляет REPORT.md
SRS_CSHARP=1 python3 -m unittest tests.test_csharp_bridge   # быстрая сверка C# и Python, ~5 с
python3 -m srs_sim.golden                                   # пересоздать эталон для C#-теста
```
