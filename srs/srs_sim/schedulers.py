"""Два планировщика повторений.

Оба отвечают на один вопрос: через сколько дней показать элемент снова,
если известна его история ответов. Адаптивный вдобавок учится на всех
ответах (метод observe), фиксированный их игнорирует.
"""

from __future__ import annotations

import math
from typing import Protocol, Sequence

from .events import HistoryFeatures, ReviewEvent

LN2 = math.log(2.0)


class Scheduler(Protocol):
    name: str

    def next_interval(self, history: Sequence[ReviewEvent]) -> int:
        """Интервал в днях (>= 1) от последнего контакта до следующего показа."""
        ...

    def observe(self, history_before: Sequence[ReviewEvent], event: ReviewEvent) -> None:
        """Сообщить планировщику результат очередного показа."""
        ...


class FixedIntervalScheduler:
    """Базовая линия: лестница фиксированных интервалов.

    Верный ответ поднимает элемент на ступень выше, ошибка возвращает на первую.
    Ступень = длина хвоста верных ответов.
    """

    DEFAULT_STEPS = (1, 3, 7, 14, 30, 60, 120)

    def __init__(self, steps: Sequence[int] = DEFAULT_STEPS) -> None:
        if not steps or any(s < 1 for s in steps):
            raise ValueError("steps must be a non-empty sequence of positive ints")
        self.steps = tuple(steps)
        self.name = "fixed"

    def next_interval(self, history: Sequence[ReviewEvent]) -> int:
        streak = HistoryFeatures.from_history(history).streak
        return self.steps[min(streak, len(self.steps) - 1)]

    def observe(self, history_before: Sequence[ReviewEvent], event: ReviewEvent) -> None:
        pass


class AdaptiveScheduler:
    """Адаптивный планировщик по модели half-life regression (Settles & Meeder, 2016).

    Вероятность вспомнить элемент через t дней:
        p = 2 ** (-t / h),   h = 2 ** (theta · x)
    где h — «период полураспада» памяти в днях, x — признаки истории ответов.
    Вероятность забывания = 1 - p.

    Веса theta общие для всех элементов и подбираются онлайн-градиентным спуском
    по квадрату ошибки (p - y)^2 после каждого ответа y ∈ {0, 1}.

    Следующий показ назначается на момент, когда предсказанная вероятность
    вспомнить опустится до target_recall:  t = h * log2(1 / target_recall).
    """

    FEATURE_NAMES = ("bias", "sqrt_correct", "sqrt_wrong", "last_wrong")
    MIN_LOG2_HALF_LIFE = -4.0   # h >= 1/16 дня
    MAX_LOG2_HALF_LIFE = 12.0   # h <= 4096 дней

    def __init__(
        self,
        target_recall: float = 0.9,
        learning_rate: float = 0.05,
        l2: float = 1e-4,
        min_interval: int = 1,
        max_interval: int = 365,
        initial_weights: Sequence[float] | None = None,
    ) -> None:
        if not 0.0 < target_recall < 1.0:
            raise ValueError("target_recall must be in (0, 1)")
        self.target_recall = target_recall
        self.learning_rate = learning_rate
        self.l2 = l2
        self.min_interval = min_interval
        self.max_interval = max_interval
        # Старт: h = 1 день для нового элемента, остальные признаки нейтральны.
        self.weights = list(initial_weights) if initial_weights is not None else [0.0] * len(self.FEATURE_NAMES)
        if len(self.weights) != len(self.FEATURE_NAMES):
            raise ValueError("wrong number of initial weights")
        self.name = f"adaptive@{target_recall:g}"
        # Предсказания до ответа и сами ответы — для оценки калибровки (Brier score).
        self.predictions: list[tuple[float, bool]] = []

    # --- модель ---

    @staticmethod
    def features(history: Sequence[ReviewEvent]) -> list[float]:
        f = HistoryFeatures.from_history(history)
        return [
            1.0,
            math.sqrt(f.n_correct),
            math.sqrt(f.n_wrong),
            1.0 if f.last_correct is False else 0.0,
        ]

    def half_life(self, history: Sequence[ReviewEvent]) -> float:
        x = self.features(history)
        log2_h = sum(w * xi for w, xi in zip(self.weights, x))
        # Ограничиваем, чтобы ранний шум в весах не дал h = 0 или бесконечность.
        log2_h = max(self.MIN_LOG2_HALF_LIFE, min(log2_h, self.MAX_LOG2_HALF_LIFE))
        return 2.0 ** log2_h

    def recall_probability(self, history: Sequence[ReviewEvent], elapsed_days: float) -> float:
        return 2.0 ** (-elapsed_days / self.half_life(history))

    def forgetting_probability(self, history: Sequence[ReviewEvent], elapsed_days: float) -> float:
        return 1.0 - self.recall_probability(history, elapsed_days)

    def forgetting_many(self, day: int, entries: Sequence[tuple[int, Sequence[ReviewEvent], float]]) -> list[float]:
        """Вероятности забыть для очереди дня day: (id элемента, история, дней с прошлого контакта)."""
        return [self.forgetting_probability(h, t) for _, h, t in entries]

    # --- интерфейс планировщика ---

    def next_interval(self, history: Sequence[ReviewEvent]) -> int:
        h = self.half_life(history)
        t = h * math.log2(1.0 / self.target_recall)
        return int(max(self.min_interval, min(self.max_interval, round(t))))

    def observe(self, history_before: Sequence[ReviewEvent], event: ReviewEvent) -> None:
        x = self.features(history_before)
        h = self.half_life(history_before)
        t = event.elapsed_days
        p = 2.0 ** (-t / h)
        y = 1.0 if event.was_correct else 0.0
        self.predictions.append((p, event.was_correct))

        # dp/dtheta = p * ln2 * (t / h) * ln2 * x
        common = 2.0 * (p - y) * p * LN2 * LN2 * t / h
        for i, xi in enumerate(x):
            grad = common * xi + self.l2 * self.weights[i]
            # Клиппинг градиента: защита от скачков на редких длинных интервалах.
            grad = max(-1.0, min(1.0, grad))
            self.weights[i] -= self.learning_rate * grad

    def brier_score(self) -> float | None:
        if not self.predictions:
            return None
        return sum((p - (1.0 if y else 0.0)) ** 2 for p, y in self.predictions) / len(self.predictions)
