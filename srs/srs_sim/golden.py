"""Эталон для перекрёстного теста C#-порта модели (tests/EasyLangApi.Tests, HalfLifeRegressionTests).

Детерминированный сценарий без случайности: 20 элементов, 120 дней, ответ «помнил» задаётся
правилом от номера элемента и числа его повторений. Python-планировщик проходит сценарий,
а файл фиксирует его решения. C#-тест проходит тот же сценарий своим AdaptiveScheduler
и сверяет каждое решение.

    python3 -m srs_sim.golden   # перезаписывает ../tests/EasyLangApi.Tests/Assets/hlr-golden.json
"""

from __future__ import annotations

import json
from pathlib import Path

from .events import ReviewEvent
from .schedulers import AdaptiveScheduler

TARGET = 0.8
DAYS = 120
ITEMS = 20

OUT = Path(__file__).resolve().parents[2] / "tests" / "EasyLangApi.Tests" / "Assets" / "hlr-golden.json"


def intro_day(item: int) -> int:
    return 0 if item < 10 else 3


def answer(item: int, reviews_before: int) -> bool:
    return (item * 7 + reviews_before * 3) % 5 != 0


def run() -> dict:
    s = AdaptiveScheduler(target_recall=TARGET)
    hist = {i: [] for i in range(ITEMS)}
    last = {i: intro_day(i) for i in range(ITEMS)}
    due = {i: intro_day(i) + s.next_interval([]) for i in range(ITEMS)}
    reviews = []
    for day in range(DAYS):
        for _, item in sorted((d, i) for i, d in due.items() if d <= day):
            ok = answer(item, len(hist[item]))
            ev = ReviewEvent(item, day, day - last[item], ok)
            # Сравниваем вероятность забывания: её отдаёт API приложения (forgettingProbability).
            forgetting = s.forgetting_probability(hist[item], ev.elapsed_days)
            s.observe(hist[item], ev)
            hist[item].append(ev)
            last[item] = day
            interval = s.next_interval(hist[item])
            due[item] = day + interval
            reviews.append({"day": day, "item": item, "correct": ok, "forgetting": forgetting, "interval": interval})
    return {
        "target": TARGET,
        "days": DAYS,
        "items": ITEMS,
        "rule": "intro = 0 if item < 10 else 3; correct = (item*7 + reviews_before*3) % 5 != 0",
        "weights": s.weights,
        "reviews": reviews,
    }


def dumps(data: dict) -> str:
    """JSON с одной записью на строку: компактно и читаемо в diff."""
    head = {k: v for k, v in data.items() if k != "reviews"}
    lines = [json.dumps(head, ensure_ascii=False)[:-1] + ', "reviews": [']
    lines += [json.dumps(r, separators=(",", ":")) + "," for r in data["reviews"]]
    lines[-1] = lines[-1].rstrip(",")
    lines.append("]}")
    return "\n".join(lines) + "\n"


def main() -> None:
    OUT.write_text(dumps(run()), encoding="utf-8")
    print(f"записано в {OUT}")


if __name__ == "__main__":
    main()
