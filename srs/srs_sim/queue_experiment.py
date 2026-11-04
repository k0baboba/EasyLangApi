"""Порядок очереди повторений: по дате или по вероятности забыть.

srs/REPORT.md показал: при лимите в 60 повторений в день адаптивный планировщик проигрывает
фиксированной лестнице, а очередь в обоих случаях идёт по дате. Здесь тот же эксперимент
(ученик, seed'ы, сценарии), но у адаптивного планировщика очередь дня отсортирована по
предсказанной вероятности забыть — сначала самые рискованные. Фиксированный планировщик
вероятностей не знает, его очередь остаётся по дате.

    python3 -m srs_sim.queue_experiment   # ~20 с, обновляет блок QUEUE в REPORT.md
"""

from __future__ import annotations

import argparse
from dataclasses import replace
from pathlib import Path

from .experiment import ADAPTIVE_TARGETS, SCENARIOS, Scenario, Summary, seeds
from .report import update_report
from .schedulers import AdaptiveScheduler, FixedIntervalScheduler
from .simulation import SimResult, run_many

REPORT = Path(__file__).resolve().parent.parent / "REPORT.md"
QUEUE_BEGIN = "<!-- QUEUE:BEGIN — генерируется `python -m srs_sim.queue_experiment`, не править руками -->"
QUEUE_END = "<!-- QUEUE:END -->"


def with_order(scenario: Scenario, order: str) -> Scenario:
    return replace(scenario, config=replace(scenario.config, queue_order=order))


def run(seed_list: list[int], targets=ADAPTIVE_TARGETS, scenarios=SCENARIOS) -> dict:
    out = {}
    for sc in scenarios:
        fixed = Summary.of("fixed", run_many(FixedIntervalScheduler, sc.config, seed_list))
        rows = []
        for t in targets:
            make = lambda t=t: AdaptiveScheduler(target_recall=t)
            by_due = Summary.of(f"adaptive@{t:g}", run_many(make, sc.config, seed_list))
            by_risk = Summary.of(f"adaptive@{t:g}", run_many(make, with_order(sc, "forgetting").config, seed_list))
            rows.append((t, by_due, by_risk))
        out[sc.key] = (sc, fixed, rows)
    return out


def _known(r: SimResult) -> float:
    return r.known_items


def _retention(r: SimResult) -> float:
    return r.final_retention


def render(results: dict, seed_list: list[int]) -> str:
    lines = [
        QUEUE_BEGIN,
        "",
        f"Seed'ы: {seed_list[0]}…{seed_list[-1]} ({len(seed_list)} парных прогонов на строку). "
        "«дата» — очередь по сроку, «p забыть» — по предсказанной вероятности забыть. "
        "Значения — среднее по прогонам.",
    ]
    for key, (sc, fixed, rows) in results.items():
        lines += [
            "",
            f"#### {sc.title}",
            "",
            f"`fixed` (очередь по дате): слов с p ≥ 0.9 — {fixed.known_items.mean:.0f}, "
            f"средняя R в конце — {fixed.final_retention.mean * 100:.1f} %, "
            f"просрочено в конце — {fixed.backlog_end.mean:.0f}.",
            "",
            "| Цель T | Слов с p ≥ 0.9: дата → p забыть | Seed'ов, где p забыть лучше даты | "
            "Средняя R в конце, %: дата → p забыть | Просрочено в конце: дата → p забыть | "
            "Повторений: дата → p забыть | Seed'ов, где p забыть лучше fixed |",
            "|---|---:|---:|---:|---:|---:|---:|",
        ]
        for t, by_due, by_risk in rows:
            lines.append(
                f"| {t:g} | {by_due.known_items.mean:.0f} → {by_risk.known_items.mean:.0f} "
                f"| {by_risk.wins_over(by_due, _known)}/{len(seed_list)} "
                f"| {by_due.final_retention.mean * 100:.1f} → {by_risk.final_retention.mean * 100:.1f} "
                f"| {by_due.backlog_end.mean:.0f} → {by_risk.backlog_end.mean:.0f} "
                f"| {by_due.reviews.mean:.0f} → {by_risk.reviews.mean:.0f} "
                f"| {by_risk.wins_over(fixed, _known)}/{len(seed_list)} |")
    lines += ["", QUEUE_END]
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description="Очередь по дате или по вероятности забыть")
    parser.add_argument("--no-write", action="store_true", help="только напечатать, REPORT.md не трогать")
    args = parser.parse_args()
    seed_list = seeds()
    text = render(run(seed_list), seed_list)
    print(text)
    if not args.no_write:
        update_report(REPORT, text, QUEUE_BEGIN, QUEUE_END)
        print(f"\nзаписано в {REPORT}")


if __name__ == "__main__":
    main()
