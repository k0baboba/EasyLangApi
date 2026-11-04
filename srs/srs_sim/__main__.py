"""Запуск эксперимента: python -m srs_sim [--out REPORT.md]"""

from __future__ import annotations

import argparse
from pathlib import Path

from .experiment import N_RUNS, SCENARIOS, SEED, run_experiment, seeds
from .report import render_auto, update_report


def main() -> None:
    parser = argparse.ArgumentParser(description="Сравнение фиксированного и адаптивного планировщиков")
    parser.add_argument("--seed", type=int, default=SEED, help=f"первый seed (по умолчанию {SEED})")
    parser.add_argument("--runs", type=int, default=N_RUNS, help=f"число прогонов (по умолчанию {N_RUNS})")
    parser.add_argument("--out", type=Path, default=Path(__file__).resolve().parent.parent / "REPORT.md")
    args = parser.parse_args()

    seed_list = seeds(args.seed, args.runs)
    results = run_experiment(SCENARIOS, seed_list)
    auto = render_auto(results, SCENARIOS, seed_list)
    update_report(args.out, auto)
    print(auto)
    print(f"\nзаписано в {args.out}")


if __name__ == "__main__":
    main()
