"""Синтетический ученик.

Скрытая «истинная» память устроена по мотивам модели DSR
(difficulty / stability / retrievability), на которой построен FSRS:

* у каждого элемента своя сложность D ∈ [1, 10], планировщик её не видит;
* стабильность S — число дней, за которое вероятность вспомнить падает до 90 %;
* вероятность вспомнить через t дней: R = 0.9 ** (t / S);
* успешное повторение увеличивает S, и тем сильнее, чем ниже было R
  (эффект интервалов: трудное припоминание укрепляет память сильнее);
* ошибка резко снижает S.

Форма модели намеренно отличается от модели адаптивного планировщика
(экспонента с основанием 2 и признаки истории), чтобы планировщик не получил
«правильный ответ» даром. Параметры подобраны вручную, а не обучены на данных.
"""

from __future__ import annotations

import math
import random
from dataclasses import dataclass


@dataclass(frozen=True)
class LearnerParams:
    # Начальная стабильность после первого знакомства, дней (для D = 5).
    initial_stability: float = 1.5
    # Насколько сильно сложность влияет на начальную стабильность.
    initial_difficulty_effect: float = 0.15
    # Рост стабильности при успехе: S' = S * (1 + g * (11 - D) * S^-decay * (e^(spacing*(1-R)) - 1))
    growth: float = 4.0
    stability_decay: float = 0.1
    spacing_effect: float = 2.0
    # Стабильность после ошибки: доля от прежней, но не больше прежней.
    lapse_factor: float = 0.3
    min_stability: float = 0.3
    # Вероятность случайно ошибиться, даже помня элемент (невнимательность, опечатка).
    slip: float = 0.02
    # Распределение сложности: Beta(a, b), растянутое на [1, 10].
    difficulty_alpha: float = 2.0
    difficulty_beta: float = 2.0


@dataclass
class ItemMemory:
    difficulty: float
    stability: float
    last_seen_day: int


class SyntheticLearner:
    """Два независимых генератора: один для сложности элементов, другой для ответов.

    Поэтому при одном seed разные планировщики получают одинаковый набор
    элементов той же сложности (парное сравнение, меньше разброс между прогонами).
    """

    def __init__(self, seed: int, params: LearnerParams | None = None) -> None:
        self.params = params or LearnerParams()
        self.items: dict[int, ItemMemory] = {}
        self._rng_items = random.Random(f"items-{seed}")
        self._rng_answers = random.Random(f"answers-{seed}")

    def introduce(self, item_id: int, day: int) -> None:
        """Первое знакомство: пользователь посмотрел разбор фразы (Lookup)."""
        p = self.params
        d = 1.0 + 9.0 * self._rng_items.betavariate(p.difficulty_alpha, p.difficulty_beta)
        s0 = p.initial_stability * math.exp(-p.initial_difficulty_effect * (d - 5.0))
        self.items[item_id] = ItemMemory(difficulty=d, stability=max(p.min_stability, s0), last_seen_day=day)

    def recall_probability(self, item_id: int, day: int) -> float:
        m = self.items[item_id]
        t = day - m.last_seen_day
        return 0.9 ** (t / m.stability)

    def review(self, item_id: int, day: int) -> bool:
        """Показ элемента: ученик отвечает, память обновляется. Возвращает was_correct."""
        p = self.params
        m = self.items[item_id]
        r = self.recall_probability(item_id, day)
        correct = self._rng_answers.random() < r * (1.0 - p.slip)
        if correct:
            gain = p.growth * (11.0 - m.difficulty) / 10.0 * m.stability ** (-p.stability_decay) \
                * (math.exp(p.spacing_effect * (1.0 - r)) - 1.0)
            m.stability = m.stability * (1.0 + gain)
        else:
            m.stability = max(p.min_stability, min(m.stability, m.stability * p.lapse_factor))
        m.last_seen_day = day
        return correct
