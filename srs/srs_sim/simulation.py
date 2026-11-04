"""Прогон одного планировщика на одном синтетическом ученике."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Callable

from .events import ReviewEvent
from .learner import LearnerParams, SyntheticLearner
from .schedulers import Scheduler


@dataclass(frozen=True)
class SimConfig:
    days: int = 180                 # длительность эксперимента
    intro_days: int = 90            # в первые intro_days дней появляются новые слова
    new_items_per_day: int = 10     # сколько новых слов в день пользователь разбирает в игре
    daily_budget: int | None = None  # максимум повторений в день; None — без ограничения
    recall_threshold: float = 0.9   # «знает слово», если истинная вероятность вспомнить не ниже


@dataclass
class SimResult:
    scheduler: str
    seed: int
    n_items: int
    total_reviews: int
    correct_reviews: int
    final_retention: float          # средняя истинная вероятность вспомнить в последний день
    known_items: int                # сколько слов на последний день вспоминаются с p >= порога
    backlog_end: int                # просроченные, но не показанные из-за лимита элементы
    retention_by_day: list[float] = field(default_factory=list)
    reviews_by_day: list[int] = field(default_factory=list)
    brier: float | None = None      # калибровка прогнозов (только у адаптивного)

    @property
    def accuracy(self) -> float:
        return self.correct_reviews / self.total_reviews if self.total_reviews else 0.0

    @property
    def known_per_100_reviews(self) -> float:
        return 100.0 * self.known_items / self.total_reviews if self.total_reviews else 0.0


def simulate(
    scheduler: Scheduler,
    config: SimConfig,
    seed: int,
    learner_params: LearnerParams | None = None,
) -> SimResult:
    learner = SyntheticLearner(seed, learner_params)
    history: dict[int, list[ReviewEvent]] = {}
    last_contact: dict[int, int] = {}
    due: dict[int, int] = {}

    retention_by_day: list[float] = []
    reviews_by_day: list[int] = []
    total = correct = 0
    next_id = 0

    for day in range(config.days):
        # 1. Замер знаний на начало дня (до повторений и новых слов).
        retention_by_day.append(_mean_recall(learner, day))

        # 2. Повторения: сначала самые просроченные, при равенстве — по id.
        queue = sorted((d, i) for i, d in due.items() if d <= day)
        if config.daily_budget is not None:
            queue = queue[: config.daily_budget]
        for _, item in queue:
            h = history[item]
            event = ReviewEvent(
                item_id=item,
                day=day,
                elapsed_days=day - last_contact[item],
                was_correct=learner.review(item, day),
            )
            scheduler.observe(h, event)
            h.append(event)
            last_contact[item] = day
            due[item] = day + scheduler.next_interval(h)
            total += 1
            correct += event.was_correct
        reviews_by_day.append(len(queue))

        # 3. Новые слова из игры.
        if day < config.intro_days:
            for _ in range(config.new_items_per_day):
                item = next_id
                next_id += 1
                learner.introduce(item, day)
                history[item] = []
                last_contact[item] = day
                due[item] = day + scheduler.next_interval([])

    final_day = config.days
    recalls = [learner.recall_probability(i, final_day) for i in learner.items]
    return SimResult(
        scheduler=scheduler.name,
        seed=seed,
        n_items=len(recalls),
        total_reviews=total,
        correct_reviews=correct,
        final_retention=sum(recalls) / len(recalls) if recalls else 0.0,
        known_items=sum(1 for r in recalls if r >= config.recall_threshold),
        backlog_end=sum(1 for d in due.values() if d < final_day),
        retention_by_day=retention_by_day,
        reviews_by_day=reviews_by_day,
        # Прогноз есть у любого планировщика с brier_score (Python-адаптивный и мост к C#).
        brier=scheduler.brier_score() if hasattr(scheduler, "brier_score") else None,
    )


def _mean_recall(learner: SyntheticLearner, day: int) -> float:
    if not learner.items:
        return 0.0
    return sum(learner.recall_probability(i, day) for i in learner.items) / len(learner.items)


def run_many(
    make_scheduler: Callable[[], Scheduler],
    config: SimConfig,
    seeds: list[int],
    learner_params: LearnerParams | None = None,
) -> list[SimResult]:
    """Новый планировщик на каждый seed: адаптивный не переносит знания между учениками."""
    return [simulate(make_scheduler(), config, s, learner_params) for s in seeds]
