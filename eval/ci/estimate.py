"""Оценка полного прогона по пробе: python3 ci/estimate.py out/probe

Пишет out/probe/timing.md и печатает для $GITHUB_OUTPUT:
  shards=N
  shard_list=[0, 1, ...]
Код выхода 1, если какой-то вариант LLM в пробе не дал ни одного ответа: полный прогон бессмыслен.
"""

from __future__ import annotations

import json
import math
import os
import sys
from pathlib import Path

FULL_ITEMS = 150
SETUP_MINUTES = 15          # установка Ollama, скачивание двух моделей, запас
TARGET = float(os.environ.get("TARGET_SHARD_MINUTES", "240"))
MAX_SHARDS = int(os.environ.get("MAX_SHARDS", "12"))


def read(path: Path) -> dict:
    out = {}
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.strip():
                r = json.loads(line)
                out[(r["id"], r["variant"])] = r
    return out


def mean(xs):
    return sum(xs) / len(xs) if xs else None


def main(probe: Path) -> int:
    answers, judgments = read(probe / "answers.jsonl"), read(probe / "judgments.jsonl")
    variants = sorted({k[1] for k in answers})
    lines = ["# Проба: время на запрос", "", f"Папка: `{probe.name}`. Числа измерены в этой пробе.", "",
             "| Шаг | Успешно | Ошибок | Среднее, с | Максимум, с |", "|---|---:|---:|---:|---:|"]
    per_item = 0.0
    failed_llm = []
    for v in variants:
        rs = [r for k, r in answers.items() if k[1] == v]
        ok = [r["elapsed_ms"] / 1000 for r in rs if r["ok"]]
        if v != "dictionary" and not ok:
            failed_llm.append(v)
        m = mean(ok) or 0.0
        per_item += m
        lines.append(f"| ответ `{v}` | {len(ok)} | {len(rs) - len(ok)} | {m:.1f} | {max(ok, default=0):.1f} |")
    js = [r for r in judgments.values() if r.get("ok") and "elapsed_ms" in r]
    jt = [r["elapsed_ms"] / 1000 for r in js]
    jm = mean(jt) or 0.0
    lines.append(f"| вердикт судьи | {len(jt)} | {len(judgments) - len(js)} | {jm:.1f} | {max(jt, default=0):.1f} |")

    total_min = FULL_ITEMS * (per_item + len(variants) * jm) / 60
    work_per_shard = max(TARGET - SETUP_MINUTES, 30)
    shards = max(1, min(MAX_SHARDS, math.ceil(total_min / work_per_shard)))
    lines += [
        "",
        f"Оценка полного прогона ({FULL_ITEMS} фраз × {len(variants)} варианта + судья): "
        f"**{total_min:.0f} мин** чистого времени запросов.",
        f"Делим на **{shards}** параллельных задач: примерно {total_min / shards + SETUP_MINUTES:.0f} мин на задачу "
        f"с установкой (цель — не больше {TARGET:.0f} мин, предел задачи 340).",
    ]
    if total_min / shards + SETUP_MINUTES > 330:
        lines.append("**Внимание:** даже при максимуме задач часть может не уложиться; продолжение из results это добирает.")
    if failed_llm:
        lines.append(f"**Проба провалена:** нет ни одного ответа у {', '.join(failed_llm)} — полный прогон не запускаю.")
    (probe / "timing.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines), file=sys.stderr)
    print(f"shards={shards}")
    print(f"shard_list={json.dumps(list(range(shards)))}")
    return 1 if failed_llm else 0


if __name__ == "__main__":
    sys.exit(main(Path(sys.argv[1])))
