"""Сводка results.md по строкам с вердиктами. Числа берутся только из файлов прогона."""

from __future__ import annotations

from .dataset import CATEGORIES
from .metrics import Row, paired, variant_stats

NAMES = {
    "dictionary": "словарь",
    "llm_plain": "LLM без контекста",
    "llm_context": "LLM с контекстом",
}
CATEGORY_NAMES = {
    "slang": "сленг",
    "polysemy": "многозначные слова",
    "control": "контроль (верен буквальный смысл)",
}


def _pct(x: float | None) -> str:
    return "—" if x is None else f"{100 * x:.1f} %"


def _acc(s) -> str:
    if s.accuracy is None:
        return "—"
    lo, hi = s.ci
    return f"{_pct(s.accuracy)} [{100 * lo:.0f}–{100 * hi:.0f}]"


def render_report(rows: list[Row], variants: tuple[str, ...], meta: dict, errors: list[str]) -> str:
    llm = meta.get("llm") or {}
    judge = meta.get("judge") or {}
    out = [
        "# Результаты прогона",
        "",
        "Сгенерировано скриптом `python -m easylang_eval` по ответам сервисов из этого прогона.",
        "",
        f"- Дата: {meta['generated_at']}",
        f"- Набор: `{meta['dataset']}` ({meta['items']} фраз), sha256 `{meta['dataset_sha256']}`",
        f"- Модель разбора: `{llm.get('model', '—')}` по адресу `{llm.get('base_url', '—')}`",
        f"- Судья: `{judge.get('model', '—')}` по адресу `{judge.get('base_url', '—')}`",
        f"- Словарь: `{meta.get('dictionary_url')}`",
    ]
    if llm and judge and llm.get("model") == judge.get("model") and llm.get("base_url") == judge.get("base_url"):
        out.append("- **Судья — та же модель, что и разбирала фразы.** Возможна предвзятость к своим ответам.")

    out += [
        "",
        "## Итог",
        "",
        "Точность — доля фраз, где судья сопоставил объяснение с правильным смыслом. "
        "В квадратных скобках — 95 % доверительный интервал Уилсона. Ошибки сервиса в точность не входят.",
        "",
        "| Вариант | Оценено | Ошибок сервиса | Верно | Точность [95 % ДИ] | Неверный смысл | Ни один | Ответ судьи не разобран | Источник «знает» фразу |",
        "|---|---:|---:|---:|---|---:|---:|---:|---:|",
    ]
    for v in variants:
        s = variant_stats(rows, v)
        out.append(
            f"| {NAMES.get(v, v)} | {s.scored} | {s.errors} | {s.correct} | {_acc(s)} "
            f"| {s.kinds['distractor']} | {s.kinds['none']} | {s.kinds['unparsed']} | {s.found} |"
        )

    out += ["", "## По категориям", "", "| Категория | " + " | ".join(NAMES.get(v, v) for v in variants) + " |",
            "|---|" + "---|" * len(variants)]
    for c in CATEGORIES:
        cells = []
        for v in variants:
            s = variant_stats(rows, v, c)
            cells.append(f"{_acc(s)}, n={s.scored}" if s.total else "—")
        out.append(f"| {CATEGORY_NAMES[c]} | " + " | ".join(cells) + " |")

    controls = [variant_stats(rows, v, "control") for v in variants]
    if any(s.scored for s in controls):
        out += ["", "В контрольных фразах «неверный смысл» почти всегда означает сленговое прочтение там, "
                "где имелось в виду буквальное:", ""]
        for s in controls:
            out.append(f"- {NAMES.get(s.variant, s.variant)}: {s.kinds['distractor']} из {s.scored}")

    pairs = [(a, b) for a, b in (("llm_context", "llm_plain"), ("llm_context", "dictionary"), ("llm_plain", "dictionary"))
             if a in variants and b in variants]
    if pairs:
        out += [
            "",
            "## Парное сравнение",
            "",
            "Только фразы, где оценены оба варианта. p — точный тест Макнемара по фразам, где прав только один.",
            "",
            "| A против B | Оба верно | Только A | Только B | Оба неверно | p |",
            "|---|---:|---:|---:|---:|---:|",
        ]
        for a, b in pairs:
            p = paired(rows, a, b)
            out.append(f"| {NAMES[a]} против {NAMES[b]} | {p.both} | {p.only_a} | {p.only_b} | {p.neither} | {p.p_value:.3g} |")

    if errors:
        out += ["", f"## Ошибки ({len(errors)})", ""]
        out += [f"- {e}" for e in errors[:20]]
        if len(errors) > 20:
            out.append(f"- … и ещё {len(errors) - 20}, полностью — в answers.jsonl и judgments.jsonl")
        out += ["", "Перезапуск с той же папкой `--out` повторит только неудачные запросы."]

    out += [
        "",
        "## Как читать",
        "",
        "- Судья — тоже языковая модель. Проверьте вручную случайные строки из `results.csv` "
        "(колонка `manual_ok`), прежде чем опираться на числа.",
        "- У словаря оценивается первое значение — то, что приложение показывает первым.",
        "",
    ]
    return "\n".join(out)
