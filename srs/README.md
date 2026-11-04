# srs — эксперимент с планировщиками повторений

Отдельный проект на Python (только стандартная библиотека, 3.11+). Он не зависит от основного
приложения на ASP.NET Core, и основное приложение не зависит от него. Python выбран специально:
корневой `EasyLangApi.csproj` включает в сборку все `*.cs` из подпапок, поэтому C#-проект
в `srs/` попал бы в основное приложение.

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
tests/                  unittest
```

```bash
cd srs
python3 -m unittest discover -s tests -t .                  # быстрые тесты, <1 с
python3 -m srs_sim                                          # полный эксперимент, ~30 с, обновляет REPORT.md
SRS_FULL=1 python3 -m unittest tests.test_reproducibility   # REPORT.md совпадает с новым прогоном
```
