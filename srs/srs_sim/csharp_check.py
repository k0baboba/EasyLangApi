"""Прогон адаптивного планировщика основного приложения (C#) на тех же условиях, что и Python-версия.

Решения принимает EasyLangApi.Review.AdaptiveScheduler — тот же класс, что работает в приложении.
Он запускается в процессе-мосте (srs/bridge) и общается с симуляцией по строкам JSON.
Ученик, seed'ы, сценарии и порядок очереди — из srs_sim без изменений. Для каждого прогона
рядом идёт Python-планировщик с той же целью, и сравниваются:

* все решения об интервалах (последовательность целиком);
* все прогнозы вероятности забыть до ответа (точное равенство чисел double);
* итоговые метрики прогона.

    python3 -m srs_sim.csharp_check            # собирает мост, ~несколько минут, обновляет блок в REPORT.md
    python3 -m srs_sim.csharp_check --quick    # 2 seed'а, короткая симуляция — проверить, что всё работает
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Sequence

from .events import ReviewEvent
from .experiment import ADAPTIVE_TARGETS, SCENARIOS, Scenario, Stat, seeds
from .report import update_report
from .schedulers import AdaptiveScheduler
from .simulation import SimResult, simulate

SRS_DIR = Path(__file__).resolve().parent.parent
BRIDGE_PROJECT = SRS_DIR / "bridge" / "SchedulerBridge.csproj"
BRIDGE_DLL = SRS_DIR / "bridge" / "bin" / "Release" / "net10.0" / "SchedulerBridge.dll"
REPORT = SRS_DIR / "REPORT.md"

CSHARP_BEGIN = "<!-- CSHARP:BEGIN — генерируется `python -m srs_sim.csharp_check`, не править руками -->"
CSHARP_END = "<!-- CSHARP:END -->"


def build_bridge() -> list[str]:
    subprocess.run(["dotnet", "build", str(BRIDGE_PROJECT), "-c", "Release", "--nologo", "-v", "quiet"], check=True)
    return ["dotnet", str(BRIDGE_DLL)]


class CSharpAdaptiveScheduler:
    """Планировщик для simulate(), решения которого принимает C#-код приложения."""

    def __init__(self, target_recall: float, bridge_cmd: Sequence[str]) -> None:
        self.name = f"csharp@{target_recall:g}"
        self._proc = subprocess.Popen(
            list(bridge_cmd), stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, bufsize=1)
        self._call({"op": "config", "target": target_recall})
        self._pending: tuple[int, int] | None = None
        self.intervals: list[int] = []
        self.forgetting: list[float] = []
        self.predictions: list[tuple[float, bool]] = []

    def _call(self, request: dict) -> dict:
        assert self._proc.stdin and self._proc.stdout
        self._proc.stdin.write(json.dumps(request) + "\n")
        response = json.loads(self._proc.stdout.readline())
        if "error" in response:
            raise RuntimeError(response["error"])
        return response

    def next_interval(self, history: Sequence[ReviewEvent]) -> int:
        if not history:
            interval = int(self._call({"op": "new"})["interval"])
        else:
            item, interval = self._pending  # решение уже пришло в ответ на observe
            assert item == history[-1].item_id
        self.intervals.append(interval)
        return interval

    def observe(self, history_before: Sequence[ReviewEvent], event: ReviewEvent) -> None:
        r = self._call({"op": "review", "item": event.item_id, "day": event.day,
                        "elapsed": event.elapsed_days, "correct": event.was_correct})
        self.forgetting.append(r["forgetting"])
        self.predictions.append((1.0 - r["forgetting"], event.was_correct))
        self._pending = (event.item_id, int(r["interval"]))

    def brier_score(self) -> float | None:
        if not self.predictions:
            return None
        return sum((p - (1.0 if y else 0.0)) ** 2 for p, y in self.predictions) / len(self.predictions)

    def close(self) -> None:
        if self._proc.stdin:
            self._proc.stdin.close()
        self._proc.wait(timeout=30)


class RecordingAdaptiveScheduler(AdaptiveScheduler):
    """Python-планировщик из srs_sim, который записывает свои решения и прогнозы для сравнения."""

    def __init__(self, target_recall: float) -> None:
        super().__init__(target_recall=target_recall)
        self.intervals: list[int] = []
        self.forgetting: list[float] = []

    def next_interval(self, history: Sequence[ReviewEvent]) -> int:
        interval = super().next_interval(history)
        self.intervals.append(interval)
        return interval

    def observe(self, history_before: Sequence[ReviewEvent], event: ReviewEvent) -> None:
        self.forgetting.append(self.forgetting_probability(history_before, event.elapsed_days))
        super().observe(history_before, event)


@dataclass(frozen=True)
class RunComparison:
    scenario: str
    target: float
    seed: int
    python: SimResult
    csharp: SimResult
    decisions: int
    same_decisions: bool
    first_diff: int | None        # номер первого различающегося решения
    same_forgetting: bool         # прогнозы совпали как числа double
    max_forgetting_diff: float


def compare_run(scenario: Scenario, target: float, seed: int, bridge_cmd: Sequence[str]) -> RunComparison:
    py = RecordingAdaptiveScheduler(target)
    py_result = simulate(py, scenario.config, seed)
    cs = CSharpAdaptiveScheduler(target, bridge_cmd)
    try:
        cs_result = simulate(cs, scenario.config, seed)
    finally:
        cs.close()

    first_diff = next((i for i, (a, b) in enumerate(zip(py.intervals, cs.intervals)) if a != b), None)
    if first_diff is None and len(py.intervals) != len(cs.intervals):
        first_diff = min(len(py.intervals), len(cs.intervals))
    diffs = [abs(a - b) for a, b in zip(py.forgetting, cs.forgetting)]
    return RunComparison(
        scenario=scenario.key, target=target, seed=seed, python=py_result, csharp=cs_result,
        decisions=len(cs.intervals), same_decisions=first_diff is None, first_diff=first_diff,
        same_forgetting=len(py.forgetting) == len(cs.forgetting) and all(d == 0.0 for d in diffs),
        max_forgetting_diff=max(diffs, default=0.0),
    )


def run_check(scenarios: Sequence[Scenario], targets: Sequence[float], seed_list: list[int],
              bridge_cmd: Sequence[str], workers: int) -> list[RunComparison]:
    jobs = [(sc, t, s) for sc in scenarios for t in targets for s in seed_list]
    # Потоки ждут ответов моста (отдельные процессы), поэтому параллелятся несмотря на GIL.
    with ThreadPoolExecutor(max_workers=workers) as pool:
        return list(pool.map(lambda job: compare_run(*job, bridge_cmd), jobs))


def _metrics(r: SimResult) -> tuple:
    return (r.total_reviews, r.correct_reviews, r.known_items, r.final_retention, r.backlog_end,
            r.retention_by_day, r.reviews_by_day)


def render(results: list[RunComparison], scenarios: Sequence[Scenario], targets: Sequence[float],
           seed_list: list[int]) -> str:
    cfg = scenarios[0].config
    lines = [
        CSHARP_BEGIN,
        "",
        f"Seed'ы: {seed_list[0]}…{seed_list[-1]} ({len(seed_list)} прогонов на строку), {cfg.days} дней, "
        f"новые слова первые {cfg.intro_days} дней по {cfg.new_items_per_day} в день. "
        f"Всего прогонов C#-планировщика: {len(results)}, решений: {sum(r.decisions for r in results)}.",
    ]
    for sc in scenarios:
        lines += [
            "",
            f"#### {sc.title}",
            "",
            "| Цель T | Решения совпали | Прогнозы p забыть совпали | Метрики прогона совпали "
            "| Повторений, Python / C# | Слов с p ≥ 0.9, Python / C# | Brier, Python / C# |",
            "|---|---:|---:|---:|---:|---:|---:|",
        ]
        for t in targets:
            rows = [r for r in results if r.scenario == sc.key and r.target == t]
            n = len(rows)
            same_dec = sum(r.same_decisions for r in rows)
            same_fc = sum(r.same_forgetting for r in rows)
            same_metrics = sum(_metrics(r.python) == _metrics(r.csharp) for r in rows)
            rev_py, rev_cs = Stat.of([r.python.total_reviews for r in rows]), Stat.of([r.csharp.total_reviews for r in rows])
            kn_py, kn_cs = Stat.of([r.python.known_items for r in rows]), Stat.of([r.csharp.known_items for r in rows])
            br_py = Stat.of([r.python.brier for r in rows]).mean
            br_cs = Stat.of([r.csharp.brier for r in rows]).mean
            lines.append(
                f"| {t:g} | {same_dec}/{n} | {same_fc}/{n} | {same_metrics}/{n} "
                f"| {rev_py.mean:.0f} / {rev_cs.mean:.0f} | {kn_py.mean:.0f} / {kn_cs.mean:.0f} "
                f"| {br_py:.3f} / {br_cs:.3f} |")
    mismatched = [r for r in results if not (r.same_decisions and r.same_forgetting)]
    lines += [""]
    if mismatched:
        lines.append("Расхождения:")
        for r in mismatched:
            lines.append(f"- {r.scenario}, T={r.target:g}, seed {r.seed}: первое различие в решении №{r.first_diff}, "
                         f"max |Δp забыть| = {r.max_forgetting_diff:.3g}")
    else:
        max_brier_diff = max(abs(r.python.brier - r.csharp.brier) for r in results)
        brier_note = ("Brier score совпал точно." if max_brier_diff == 0.0 else
                      f"Brier score отличается не больше чем на {max_brier_diff:.1e}: мост передаёт p забыть, "
                      "и p вспомнить восстанавливается как 1 − (1 − p) с округлением в последнем бите.")
        lines.append("Расхождений нет: все решения и все прогнозы C#-планировщика совпали с Python. " + brier_note)
    lines += ["", CSHARP_END]
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description="C#-планировщик приложения на тех же условиях, что и Python")
    parser.add_argument("--quick", action="store_true", help="2 seed'а, 60 дней, без записи в REPORT.md")
    parser.add_argument("--workers", type=int, default=os.cpu_count() or 2)
    args = parser.parse_args()

    bridge_cmd = build_bridge()
    if args.quick:
        scenarios = [replace(sc, config=replace(sc.config, days=60, intro_days=30)) for sc in SCENARIOS]
        seed_list, targets = seeds(n=2), (0.75, 0.9)
    else:
        scenarios, seed_list, targets = list(SCENARIOS), seeds(), ADAPTIVE_TARGETS

    results = run_check(scenarios, targets, seed_list, bridge_cmd, args.workers)
    text = render(results, scenarios, targets, seed_list)
    print(text)
    if not args.quick:
        update_report(REPORT, text, CSHARP_BEGIN, CSHARP_END)
        print(f"\nзаписано в {REPORT}")


if __name__ == "__main__":
    main()
