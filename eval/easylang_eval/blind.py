"""Слепая проверка судьи человеком.

export: из готового прогона берутся случайные N оценённых ответов (seed фиксирован),
перемешиваются и пишутся в blind.csv без названия варианта и вердикта судьи.
Человек видит то же, что судья: фразу, объяснение и пронумерованные смыслы,
и вписывает номер в колонку human_choice. Соответствие строк и вариантов —
в blind_key.json, его проверяющий не открывает.

score: читает заполненный blind.csv и blind_key.json и считает согласие с судьёй:
по номеру смысла, по «верно / неверно» и каппу Коэна для «верно / неверно».
"""

from __future__ import annotations

import csv
import json
import random
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

from .dataset import Item
from .judge import options_for
from .metrics import wilson
from .runner import read_jsonl

CSV_NAME = "blind.csv"
KEY_NAME = "blind_key.json"
INSTRUCTIONS_NAME = "INSTRUCTIONS.md"

INSTRUCTIONS = """# Слепая проверка судьи

1. Откройте `blind.csv` (Excel, LibreOffice или Google Таблицы, кодировка UTF-8).
2. В каждой строке прочитайте фразу и объяснение. Выберите номер смысла из колонки `options`,
   который совпадает с тем, что говорит объяснение. Если не подходит ни один, объяснение пустое
   или перечисляет несколько смыслов, не выбирая, ставьте номер последнего варианта.
3. Впишите номер в колонку `human_choice`. Оценивайте только текст объяснения: если оно неверное
   или расплывчатое, не «исправляйте» его своим знанием фразы. Это то же правило, что у судьи.
4. Файл `blind_key.json` не открывайте до конца проверки: в нём варианты и вердикты судьи.
5. Посчитать согласие: `python3 -m easylang_eval blind score --dir <эта папка>`.
"""


def export(items: list[Item], run_dir: Path, out_dir: Path, n: int = 60, seed: int = 2026) -> Path:
    by_id = {i.id: i for i in items}
    answers = read_jsonl(run_dir / "answers.jsonl")
    judgments = read_jsonl(run_dir / "judgments.jsonl")
    pool = sorted(
        key for key, j in judgments.items()
        if j.get("ok") and j.get("choice") is not None and key[0] in by_id
        and answers.get(key, {}).get("ok") and answers[key]["text"] == j["answer_text"]
    )
    if len(pool) < n:
        raise ValueError(f"в прогоне только {len(pool)} оценённых ответов, а нужно {n}")
    rng = random.Random(seed)
    chosen = rng.sample(pool, n)          # sample уже возвращает случайный порядок

    out_dir.mkdir(parents=True, exist_ok=True)
    key_rows = []
    with open(out_dir / CSV_NAME, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["row", "phrase", "explanation", "options", "human_choice", "comment"])
        for row, (item_id, variant) in enumerate(chosen, 1):
            it, j = by_id[item_id], judgments[(item_id, variant)]
            opts = options_for(it)
            w.writerow([row, it.phrase, j["answer_text"].strip() or "(пусто)",
                        "\n".join(f"{k + 1}. {t}" for k, t in enumerate(opts.texts)), "", ""])
            key_rows.append({"row": row, "id": item_id, "variant": variant, "judge_choice": j["choice"] + 1,
                             "correct_choice": opts.correct + 1, "none_choice": opts.none + 1})
    meta = {"run_dir": str(run_dir), "n": n, "seed": seed, "pool": len(pool), "rows": key_rows}
    (out_dir / KEY_NAME).write_text(json.dumps(meta, ensure_ascii=False, indent=1), encoding="utf-8")
    (out_dir / INSTRUCTIONS_NAME).write_text(INSTRUCTIONS, encoding="utf-8")
    return out_dir / CSV_NAME


@dataclass(frozen=True)
class Agreement:
    n: int                   # строк с ответом человека
    skipped: int             # пустые или нечисловые human_choice
    exact: int               # тот же номер смысла
    binary: int              # то же «верно / неверно»
    kappa: float | None      # каппа Коэна для «верно / неверно»
    confusion: Counter       # (человек верно?, судья верно?) -> число
    by_variant: dict

    def as_markdown(self) -> str:
        def pct(k: int) -> str:
            return f"{k}/{self.n} ({100 * k / self.n:.0f} %)" if self.n else "—"

        lines = [
            "# Согласие судьи со слепым проверяющим",
            "",
            f"Оценено строк: {self.n}, пропущено: {self.skipped}.",
            "",
            f"- Тот же номер смысла: {pct(self.exact)}",
            f"- То же «верно / неверно»: {pct(self.binary)}",
            f"- Каппа Коэна («верно / неверно»): {'—' if self.kappa is None else f'{self.kappa:.2f}'}",
            "",
            "| | судья: верно | судья: неверно |",
            "|---|---:|---:|",
            f"| проверяющий: верно | {self.confusion[(True, True)]} | {self.confusion[(True, False)]} |",
            f"| проверяющий: неверно | {self.confusion[(False, True)]} | {self.confusion[(False, False)]} |",
            "",
            "Точность вариантов на этой выборке: «слепая» — доля строк, где проверяющий выбрал правильный смысл;",
            "«судья» — доля, где правильный смысл выбрал судья. В скобках 95 % интервал Уилсона.",
            "",
            "| Вариант | Строк | Слепая оценка: верно | Судья: верно | Тот же номер | То же «верно / неверно» |",
            "|---|---:|---:|---:|---:|---:|",
        ]
        for v, (n, ex, bi, hc, jc) in sorted(self.by_variant.items()):
            lines.append(f"| {v} | {n} | {_acc(hc, n)} | {_acc(jc, n)} | {ex} | {bi} |")
        return "\n".join(lines) + "\n"


def _acc(k: int, n: int) -> str:
    if not n:
        return "—"
    lo, hi = wilson(k, n)
    return f"{k} = {100 * k / n:.0f} % [{100 * lo:.0f}–{100 * hi:.0f}]"


def cohen_kappa(pairs: list[tuple[bool, bool]]) -> float | None:
    n = len(pairs)
    if n == 0:
        return None
    po = sum(a == b for a, b in pairs) / n
    pa = sum(a for a, _ in pairs) / n
    pb = sum(b for _, b in pairs) / n
    pe = pa * pb + (1 - pa) * (1 - pb)
    return None if pe == 1 else (po - pe) / (1 - pe)


def score(blind_dir: Path) -> Agreement:
    key = {r["row"]: r for r in json.loads((blind_dir / KEY_NAME).read_text(encoding="utf-8"))["rows"]}
    with open(blind_dir / CSV_NAME, encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
    pairs, exact, skipped = [], 0, 0
    by_variant: dict[str, list[int]] = {}
    for r in rows:
        k = key.get(int(r["row"]))
        raw = (r.get("human_choice") or "").strip()
        if k is None or not raw.isdigit():
            skipped += 1
            continue
        human = int(raw)
        human_ok, judge_ok = human == k["correct_choice"], k["judge_choice"] == k["correct_choice"]
        same = human == k["judge_choice"]
        exact += same
        pairs.append((human_ok, judge_ok))
        v = by_variant.setdefault(k["variant"], [0, 0, 0, 0, 0])
        v[0] += 1
        v[1] += same
        v[2] += human_ok == judge_ok
        v[3] += human_ok
        v[4] += judge_ok
    return Agreement(
        n=len(pairs), skipped=skipped, exact=exact, binary=sum(a == b for a, b in pairs),
        kappa=cohen_kappa(pairs), confusion=Counter(pairs),
        by_variant={v: tuple(x) for v, x in by_variant.items()},
    )
