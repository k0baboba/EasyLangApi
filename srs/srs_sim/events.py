"""История ответов — то же, что таблица ReviewEvent в SPEC.md.

Время в симуляции дискретное, в днях от начала эксперимента.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Sequence


@dataclass(frozen=True)
class ReviewEvent:
    item_id: int
    day: int            # день показа (shown_at)
    elapsed_days: int   # сколько дней прошло с предыдущего контакта с элементом
    was_correct: bool


@dataclass(frozen=True)
class HistoryFeatures:
    """Сводка истории одного элемента, которой пользуются планировщики."""

    n_correct: int
    n_wrong: int
    last_correct: bool | None   # None — повторений ещё не было
    streak: int                 # сколько верных ответов подряд в конце истории

    @staticmethod
    def from_history(history: Sequence[ReviewEvent]) -> "HistoryFeatures":
        n_correct = sum(1 for e in history if e.was_correct)
        streak = 0
        for e in reversed(history):
            if not e.was_correct:
                break
            streak += 1
        return HistoryFeatures(
            n_correct=n_correct,
            n_wrong=len(history) - n_correct,
            last_correct=history[-1].was_correct if history else None,
            streak=streak,
        )
