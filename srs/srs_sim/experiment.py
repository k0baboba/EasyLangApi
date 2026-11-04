"""Описание эксперимента и агрегирование результатов по seed'ам."""

from __future__ import annotations

import statistics
from dataclasses import dataclass
from typing import Callable

from .schedulers import AdaptiveScheduler, FixedIntervalScheduler, Scheduler
from .simulation import SimConfig, SimResult, run_many

SEED = 2026
N_RUNS = 10
ADAPTIVE_TARGETS = (0.7, 0.75, 0.8, 0.85, 0.9)


@dataclass(frozen=True)
class Scenario:
    key: str
    title: str
    config: SimConfig


SCENARIOS = (
    Scenario("unlimited", "A. Без лимита повторений", SimConfig(daily_budget=None)),
    Scenario("budget60", "B. Не больше 60 повторений в день", SimConfig(daily_budget=60)),
)


def scheduler_factories() -> list[tuple[str, Callable[[], Scheduler]]]:
    out: list[tuple[str, Callable[[], Scheduler]]] = [("fixed", FixedIntervalScheduler)]
    for t in ADAPTIVE_TARGETS:
        out.append((f"adaptive@{t:g}", lambda t=t: AdaptiveScheduler(target_recall=t)))
    return out


def seeds(base: int = SEED, n: int = N_RUNS) -> list[int]:
    return list(range(base, base + n))


@dataclass(frozen=True)
class Stat:
    mean: float
    sd: float

    @staticmethod
    def of(values: list[float]) -> "Stat":
        return Stat(statistics.fmean(values), statistics.stdev(values) if len(values) > 1 else 0.0)


@dataclass(frozen=True)
class Summary:
    scheduler: str
    runs: list[SimResult]
    reviews: Stat
    accuracy: Stat
    final_retention: Stat
    known_items: Stat
    known_per_100: Stat
    backlog_end: Stat
    brier: Stat | None
    retention_by_day: list[float]   # среднее по seed'ам
    reviews_by_day: list[float]

    @staticmethod
    def of(name: str, runs: list[SimResult]) -> "Summary":
        days = len(runs[0].retention_by_day)
        briers = [r.brier for r in runs if r.brier is not None]
        return Summary(
            scheduler=name,
            runs=runs,
            reviews=Stat.of([r.total_reviews for r in runs]),
            accuracy=Stat.of([r.accuracy for r in runs]),
            final_retention=Stat.of([r.final_retention for r in runs]),
            known_items=Stat.of([r.known_items for r in runs]),
            known_per_100=Stat.of([r.known_per_100_reviews for r in runs]),
            backlog_end=Stat.of([r.backlog_end for r in runs]),
            brier=Stat.of(briers) if briers else None,
            retention_by_day=[statistics.fmean(r.retention_by_day[d] for r in runs) for d in range(days)],
            reviews_by_day=[statistics.fmean(r.reviews_by_day[d] for r in runs) for d in range(days)],
        )

    def wins_over(self, baseline: "Summary", metric: Callable[[SimResult], float]) -> int:
        """Сколько seed'ов, где этот планировщик строго лучше базового (парное сравнение)."""
        return sum(1 for a, b in zip(self.runs, baseline.runs) if metric(a) > metric(b))


def run_experiment(
    scenarios=SCENARIOS,
    seed_list: list[int] | None = None,
    factories: list[tuple[str, Callable[[], Scheduler]]] | None = None,
) -> dict[str, list[Summary]]:
    seed_list = seed_list if seed_list is not None else seeds()
    factories = factories if factories is not None else scheduler_factories()
    return {
        sc.key: [Summary.of(name, run_many(make, sc.config, seed_list)) for name, make in factories]
        for sc in scenarios
    }
