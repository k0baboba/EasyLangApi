"""Метрики по результатам прогона: точность, доверительные интервалы, парное сравнение."""

from __future__ import annotations

import math
from collections import Counter
from dataclasses import dataclass


def wilson(successes: int, n: int, z: float = 1.96) -> tuple[float, float]:
    """95 % доверительный интервал Уилсона для доли. Устойчив при малых n и долях около 0 или 1."""
    if n == 0:
        return (0.0, 0.0)
    p = successes / n
    denom = 1 + z * z / n
    centre = (p + z * z / (2 * n)) / denom
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / denom
    return (max(0.0, centre - half), min(1.0, centre + half))


def mcnemar_exact(only_a: int, only_b: int) -> float:
    """Точный двусторонний тест Макнемара: p-value для пар, где прав только один из вариантов."""
    n = only_a + only_b
    if n == 0:
        return 1.0
    k = min(only_a, only_b)
    tail = sum(math.comb(n, i) for i in range(k + 1)) / 2 ** n
    return min(1.0, 2 * tail)


@dataclass(frozen=True)
class Row:
    """Один разобранный и оценённый пример."""

    id: str
    category: str
    variant: str
    answered: bool      # сервис ответил
    judged: bool        # судья вынес вердикт
    kind: str           # correct / distractor / none / unparsed / error
    found: bool


@dataclass(frozen=True)
class VariantStats:
    variant: str
    total: int
    scored: int
    errors: int
    kinds: Counter
    found: int

    @property
    def correct(self) -> int:
        return self.kinds["correct"]

    @property
    def accuracy(self) -> float | None:
        return self.correct / self.scored if self.scored else None

    @property
    def ci(self) -> tuple[float, float]:
        return wilson(self.correct, self.scored)


def variant_stats(rows: list[Row], variant: str, category: str | None = None) -> VariantStats:
    sel = [r for r in rows if r.variant == variant and (category is None or r.category == category)]
    scored = [r for r in sel if r.answered and r.judged]
    return VariantStats(
        variant=variant,
        total=len(sel),
        scored=len(scored),
        errors=len(sel) - len(scored),
        kinds=Counter(r.kind for r in scored),
        found=sum(1 for r in sel if r.answered and r.found),
    )


@dataclass(frozen=True)
class Paired:
    a: str
    b: str
    both: int
    only_a: int
    only_b: int
    neither: int

    @property
    def p_value(self) -> float:
        return mcnemar_exact(self.only_a, self.only_b)


def paired(rows: list[Row], a: str, b: str) -> Paired:
    """Сравнение только по фразам, где оценены оба варианта."""
    def correct_by_id(v: str) -> dict[str, bool]:
        return {r.id: r.kind == "correct" for r in rows if r.variant == v and r.answered and r.judged}

    ra, rb = correct_by_id(a), correct_by_id(b)
    common = sorted(set(ra) & set(rb))
    c = Counter((ra[i], rb[i]) for i in common)
    return Paired(a, b, c[(True, True)], c[(True, False)], c[(False, True)], c[(False, False)])
