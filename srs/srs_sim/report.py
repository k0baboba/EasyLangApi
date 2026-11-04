"""Генерация автоматической части REPORT.md: таблицы метрик и графики mermaid.

Текст отчёта вне маркеров AUTO_BEGIN / AUTO_END пишется руками и при
перегенерации не меняется.
"""

from __future__ import annotations

import math
from pathlib import Path
from typing import Sequence

from .experiment import Scenario, Summary

AUTO_BEGIN = "<!-- AUTO:BEGIN — генерируется `python -m srs_sim`, не править руками -->"
AUTO_END = "<!-- AUTO:END -->"

CHART_SCHEDULERS = ("fixed", "adaptive@0.75", "adaptive@0.85")
CHART_COLORS = ("#4e79a7", "#e15759", "#59a14f")
CHART_STEP_DAYS = 10


def _pm(stat, fmt: str, scale: float = 1.0) -> str:
    return f"{stat.mean * scale:{fmt}} ± {stat.sd * scale:{fmt}}"


def render_table(summaries: Sequence[Summary]) -> str:
    base = next(s for s in summaries if s.scheduler == "fixed")
    lines = [
        "| Планировщик | Повторений всего | Доля верных ответов, % | Средняя вероятность вспомнить в конце, % "
        "| Слов с p ≥ 0.9 в конце | Таких слов на 100 повторений | Просрочено в конце | Brier score "
        "| Seed'ов, где слов с p ≥ 0.9 больше, чем у fixed |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for s in summaries:
        wins = "—" if s is base else f"{s.wins_over(base, lambda r: r.known_items)}/{len(s.runs)}"
        brier = "—" if s.brier is None else _pm(s.brier, ".3f")
        lines.append(
            f"| `{s.scheduler}` | {_pm(s.reviews, '.0f')} | {_pm(s.accuracy, '.1f', 100)} "
            f"| {_pm(s.final_retention, '.1f', 100)} | {_pm(s.known_items, '.0f')} "
            f"| {_pm(s.known_per_100, '.2f')} | {_pm(s.backlog_end, '.0f')} | {brier} | {wins} |"
        )
    return "\n".join(lines)


def render_retention_chart(summaries: Sequence[Summary], title: str) -> str:
    by_name = {s.scheduler: s for s in summaries}
    chosen = [by_name[n] for n in CHART_SCHEDULERS if n in by_name]
    days = len(chosen[0].retention_by_day)
    sample = list(range(CHART_STEP_DAYS, days, CHART_STEP_DAYS)) + [days - 1]
    palette = ", ".join(CHART_COLORS[: len(chosen)])
    values = [s.retention_by_day[d] * 100 for s in chosen for d in sample]
    y_min = max(0, math.floor((min(values) - 2) / 10) * 10)
    out = [
        "```mermaid",
        f'%%{{init: {{"themeVariables": {{"xyChart": {{"plotColorPalette": "{palette}"}}}}}}}}%%',
        "xychart-beta",
        f'    title "{title}"',
        f"    x-axis \"день\" [{', '.join(str(d) for d in sample)}]",
        f'    y-axis "средняя p вспомнить, %" {y_min} --> 100',
    ]
    for s in chosen:
        out.append(f"    line [{', '.join(f'{s.retention_by_day[d] * 100:.1f}' for d in sample)}]")
    out.append("```")
    legend = ", ".join(
        f"{color} — `{s.scheduler}`" for color, s in zip(CHART_COLORS, chosen)
    )
    return "\n".join(out) + f"\n\nЛинии (mermaid не рисует легенду): {legend}."


def render_efficiency_chart(summaries: Sequence[Summary], title: str) -> str:
    names = ", ".join(f'"{s.scheduler}"' for s in summaries)
    values = [s.known_per_100.mean for s in summaries]
    top = math.ceil(max(values) + 0.5)
    return "\n".join([
        "```mermaid",
        f'%%{{init: {{"themeVariables": {{"xyChart": {{"plotColorPalette": "{CHART_COLORS[0]}"}}}}}}}}%%',
        "xychart-beta",
        f'    title "{title}"',
        f"    x-axis [{names}]",
        f'    y-axis "слов с p ≥ 0.9 на 100 повторений" 0 --> {top}',
        f"    bar [{', '.join(f'{v:.2f}' for v in values)}]",
        "```",
    ])


def render_auto(results: dict[str, list[Summary]], scenarios: Sequence[Scenario], seed_list: list[int]) -> str:
    cfg = scenarios[0].config
    parts = [
        AUTO_BEGIN,
        "",
        f"Seed'ы: {seed_list[0]}…{seed_list[-1]} ({len(seed_list)} прогонов на каждую строку), "
        f"{cfg.days} дней, новые слова первые {cfg.intro_days} дней по {cfg.new_items_per_day} в день "
        f"(всего {cfg.intro_days * cfg.new_items_per_day}). Значения — среднее ± стандартное отклонение по прогонам.",
    ]
    for sc in scenarios:
        summaries = results[sc.key]
        parts += [
            "",
            f"### {sc.title}",
            "",
            render_table(summaries),
            "",
            render_retention_chart(summaries, f"{sc.title}: средняя вероятность вспомнить"),
            "",
            render_efficiency_chart(summaries, f"{sc.title}: эффективность"),
        ]
    parts += ["", AUTO_END]
    return "\n".join(parts)


TEMPLATE = """# Отчёт

{auto}
"""


def update_report(path: Path, auto: str) -> None:
    """Заменить автоматическую часть отчёта, сохранив текст вокруг неё."""
    if path.exists():
        text = path.read_text(encoding="utf-8")
        start, end = text.find(AUTO_BEGIN), text.find(AUTO_END)
        if start != -1 and end != -1 and start < end:
            text = text[:start] + auto + text[end + len(AUTO_END):]
        else:
            text = text.rstrip("\n") + "\n\n" + auto + "\n"
    else:
        text = TEMPLATE.format(auto=auto)
    path.write_text(text, encoding="utf-8")
