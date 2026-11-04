"""Модель планировщика на реальных ответах людей: данные Duolingo (Settles & Meeder, 2016).

Данные: settles.acl16.learning_traces.13m.csv.gz, Harvard Dataverse, лицензия CC BY-NC 4.0.
Авторы: Burr Settles, Brendan Meeder. Сами данные в репозиторий не кладутся:
скрипт читает скачанный файл и пишет только агрегаты.

Каждая строка — сессия: пользователь повторял слово (lexeme) session_seen раз, верно — session_correct.
Перед сессией известны history_seen / history_correct и delta — секунды с прошлого показа.

Модель — ровно та, что у AdaptiveScheduler (srs_sim/schedulers.py) и у приложения:
p = 2^(-t/h), h = 2^(θ·x), x = [1, √верных, √ошибок, последний раз ошибка],
онлайн-градиентный спуск по (p − y)², те же скорость, L2, клиппинг и старт θ = 0.
Отличие только в y: здесь это доля верных в сессии, а не 0/1.

Разбиение по времени: обучение на первых 80 % строк по timestamp, проверка на последних 20 %.
Признаки каждой строки считаются только по прошлому (history_* из данных и прошлые сессии того же
пользователя и слова), веса на проверке заморожены.

    python3 -m srs_sim.duolingo --data settles.acl16.learning_traces.13m.csv.gz --out out/duolingo
"""

from __future__ import annotations

import argparse
import csv
import gzip
import json
import math
import time
from array import array
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Iterable

from .schedulers import LN2, AdaptiveScheduler

SECONDS_PER_DAY = 86400.0
COLUMNS = ("p_recall", "timestamp", "delta", "user_id", "lexeme_id",
           "history_seen", "history_correct", "session_seen", "session_correct")


# --- данные ---------------------------------------------------------------

@dataclass
class Traces:
    """Столбцы в компактных массивах: 13 млн строк в списках словарей не поместились бы в память."""

    timestamp: array = field(default_factory=lambda: array("q"))
    delta_days: array = field(default_factory=lambda: array("d"))
    p_recall: array = field(default_factory=lambda: array("d"))
    n_correct: array = field(default_factory=lambda: array("l"))
    n_wrong: array = field(default_factory=lambda: array("l"))
    session_seen: array = field(default_factory=lambda: array("l"))
    session_correct: array = field(default_factory=lambda: array("l"))
    pair: array = field(default_factory=lambda: array("q"))     # номер пары (пользователь, слово)
    last_wrong: array = field(default_factory=lambda: array("b"))
    has_previous: array = field(default_factory=lambda: array("b"))
    users: int = 0
    lexemes: int = 0
    skipped: int = 0

    def __len__(self) -> int:
        return len(self.timestamp)


def open_text(path: Path):
    """gzip узнаём по сигнатуре, а не по расширению: Dataverse отдаёт файл под любым именем."""
    with open(path, "rb") as f:
        is_gzip = f.read(2) == b"\x1f\x8b"
    return gzip.open(path, "rt", encoding="utf-8", newline="") if is_gzip else open(path, encoding="utf-8", newline="")


def load(path: Path, limit: int | None = None) -> Traces:
    t = Traces()
    pairs: dict[tuple[str, str], int] = {}
    users: set[str] = set()
    lexemes: set[str] = set()
    with open_text(path) as f:
        reader = csv.DictReader(f)
        missing = [c for c in COLUMNS if c not in (reader.fieldnames or [])]
        if missing:
            raise ValueError(f"в файле нет столбцов: {', '.join(missing)}")
        for row in reader:
            try:
                ss, sc = int(row["session_seen"]), int(row["session_correct"])
                hs, hc = int(row["history_seen"]), int(row["history_correct"])
                ts, delta = int(row["timestamp"]), float(row["delta"])
                p = float(row["p_recall"])
            except (TypeError, ValueError):
                t.skipped += 1
                continue
            if ss <= 0 or not 0 <= sc <= ss or not 0 <= hc <= hs or delta < 0:
                t.skipped += 1
                continue
            key = (row["user_id"], row["lexeme_id"])
            t.pair.append(pairs.setdefault(key, len(pairs)))
            users.add(key[0])
            lexemes.add(key[1])
            t.timestamp.append(ts)
            t.delta_days.append(delta / SECONDS_PER_DAY)
            t.p_recall.append(p)
            t.n_correct.append(hc)
            t.n_wrong.append(hs - hc)
            t.session_seen.append(ss)
            t.session_correct.append(sc)
            if limit is not None and len(t) >= limit:
                break
    t.users, t.lexemes = len(users), len(lexemes)
    return t


def time_order(t: Traces) -> list[int]:
    """Номера строк по времени; при равном времени — в порядке файла (sorted стабилен)."""
    return sorted(range(len(t)), key=t.timestamp.__getitem__)


def derive_last_wrong(t: Traces, order: list[int]) -> None:
    """Признак «последний раз ошибка» по прошлой сессии той же пары в данных. Только прошлое."""
    last: dict[int, bool] = {}
    t.last_wrong = array("b", bytes(len(t)))
    t.has_previous = array("b", bytes(len(t)))
    for i in order:
        k = t.pair[i]
        prev = last.get(k)
        if prev is not None:
            t.has_previous[i] = 1
            t.last_wrong[i] = 1 if prev else 0
        last[k] = t.session_correct[i] < t.session_seen[i]


# --- модели ---------------------------------------------------------------

class HlrModel:
    """Математика AdaptiveScheduler.half_life / observe на готовых признаках.

    Совпадение с AdaptiveScheduler проверяется тестом test_duolingo.test_matches_adaptive_scheduler.
    """

    def __init__(self, name: str, features: Callable[[Traces, int], list[float]], n_features: int,
                 learning_rate: float = 0.05, l2: float = 1e-4) -> None:
        self.name, self.features = name, features
        self.weights = [0.0] * n_features
        self.learning_rate, self.l2 = learning_rate, l2

    def half_life_x(self, x: list[float]) -> float:
        log2_h = sum(w * xi for w, xi in zip(self.weights, x))
        log2_h = max(AdaptiveScheduler.MIN_LOG2_HALF_LIFE, min(log2_h, AdaptiveScheduler.MAX_LOG2_HALF_LIFE))
        return 2.0 ** log2_h

    def predict(self, t: Traces, i: int) -> float:
        return 2.0 ** (-t.delta_days[i] / self.half_life_x(self.features(t, i)))

    def update(self, t: Traces, i: int) -> None:
        x = self.features(t, i)
        h = self.half_life_x(x)
        dt = t.delta_days[i]
        p = 2.0 ** (-dt / h)
        y = t.p_recall[i]
        common = 2.0 * (p - y) * p * LN2 * LN2 * dt / h
        for k, xi in enumerate(x):
            grad = common * xi + self.l2 * self.weights[k]
            grad = max(-1.0, min(1.0, grad))
            self.weights[k] -= self.learning_rate * grad


def features_ours(t: Traces, i: int) -> list[float]:
    return [1.0, math.sqrt(t.n_correct[i]), math.sqrt(t.n_wrong[i]), float(t.last_wrong[i])]


def features_bias(t: Traces, i: int) -> list[float]:
    return [1.0]


class LogisticModel:
    """Базовая линия без формы «экспонента от времени»: логистическая регрессия, онлайн, log-loss."""

    name = "logistic"

    def __init__(self, learning_rate: float = 0.01, l2: float = 1e-6) -> None:
        self.weights = [0.0] * 5
        self.learning_rate, self.l2 = learning_rate, l2

    @staticmethod
    def x(t: Traces, i: int) -> list[float]:
        return [1.0, math.sqrt(t.n_correct[i]), math.sqrt(t.n_wrong[i]), float(t.last_wrong[i]),
                math.log1p(t.delta_days[i])]

    def predict(self, t: Traces, i: int) -> float:
        z = sum(w * v for w, v in zip(self.weights, self.x(t, i)))
        z = max(-30.0, min(30.0, z))
        return 1.0 / (1.0 + math.exp(-z))

    def update(self, t: Traces, i: int) -> None:
        x = self.x(t, i)
        g = self.predict(t, i) - t.p_recall[i]
        for k, v in enumerate(x):
            self.weights[k] -= self.learning_rate * (g * v + self.l2 * self.weights[k])


class ConstantModel:
    """Базовая линия: средняя доля верных на обучении, без учёта времени и истории."""

    name = "constant"

    def __init__(self) -> None:
        self.total = 0.0
        self.n = 0
        self.weights: list[float] = []

    def predict(self, t: Traces, i: int) -> float:
        return self.total / self.n if self.n else 0.5

    def update(self, t: Traces, i: int) -> None:
        self.total += t.p_recall[i]
        self.n += 1


# --- метрики --------------------------------------------------------------

def weighted_auc(scores: Iterable[float], pos: Iterable[int], neg: Iterable[int]) -> float | None:
    """AUC по отдельным ответам: строка с оценкой s даёт pos верных и neg неверных ответов."""
    groups: dict[float, list[int]] = {}
    for s, p, n in zip(scores, pos, neg):
        g = groups.setdefault(s, [0, 0])
        g[0] += p
        g[1] += n
    total_pos = sum(g[0] for g in groups.values())
    total_neg = sum(g[1] for g in groups.values())
    if total_pos == 0 or total_neg == 0:
        return None
    acc, neg_below = 0.0, 0
    for s in sorted(groups):
        p, n = groups[s]
        acc += p * neg_below + 0.5 * p * n
        neg_below += n
    return acc / (total_pos * total_neg)


def calibration(pred: list[float], t: Traces, rows: list[int], bins: int = 10) -> list[dict]:
    """Корзины по предсказанию, вес — число ответов в сессии."""
    acc = [[0.0, 0, 0, 0] for _ in range(bins)]   # сумма p̂·w, w, верных, строк
    for p, i in zip(pred, rows):
        b = min(bins - 1, int(p * bins))
        w = t.session_seen[i]
        acc[b][0] += p * w
        acc[b][1] += w
        acc[b][2] += t.session_correct[i]
        acc[b][3] += 1
    out = []
    for b, (pw, w, c, n) in enumerate(acc):
        if w:
            out.append({"bin": f"{b / bins:.1f}–{(b + 1) / bins:.1f}", "rows": n, "answers": w,
                        "predicted": pw / w, "observed": c / w})
    return out


def evaluate(model, t: Traces, rows: list[int]) -> dict:
    pred = [model.predict(t, i) for i in rows]
    mae = sum(abs(p - t.p_recall[i]) for p, i in zip(pred, rows)) / len(rows)
    auc = weighted_auc(pred, (t.session_correct[i] for i in rows),
                       (t.session_seen[i] - t.session_correct[i] for i in rows))
    cal = calibration(pred, t, rows)
    answers = sum(c["answers"] for c in cal)
    ece = sum(c["answers"] * abs(c["predicted"] - c["observed"]) for c in cal) / answers
    mean_pred = sum(c["predicted"] * c["answers"] for c in cal) / answers
    mean_obs = sum(c["observed"] * c["answers"] for c in cal) / answers
    return {"mae": mae, "auc": auc, "ece": ece, "mean_predicted": mean_pred, "mean_observed": mean_obs,
            "calibration": cal}


# --- прогон ---------------------------------------------------------------

def make_models() -> list:
    return [
        ConstantModel(),
        HlrModel("hlr_bias", features_bias, 1),
        LogisticModel(),
        HlrModel("hlr_ours", features_ours, 4),
    ]


def run(path: Path, train_share: float = 0.8, limit: int | None = None, log=print) -> dict:
    started = time.monotonic()
    t = load(path, limit)
    log(f"загружено строк: {len(t)}, пропущено: {t.skipped}, {time.monotonic() - started:.0f} с")
    order = time_order(t)
    derive_last_wrong(t, order)
    cut = int(len(order) * train_share)
    train, test = order[:cut], order[cut:]
    cutoff_ts = t.timestamp[test[0]] if test else None

    results = {}
    for model in make_models():
        s = time.monotonic()
        for i in train:
            model.update(t, i)
        metrics = evaluate(model, t, test)
        metrics["weights"] = [round(w, 6) for w in model.weights]
        metrics["seconds"] = round(time.monotonic() - s, 1)
        results[model.name] = metrics
        log(f"{model.name}: MAE {metrics['mae']:.4f}, AUC {metrics['auc']:.4f}, ECE {metrics['ece']:.4f}, "
            f"{metrics['seconds']} с")

    test_prev = sum(t.has_previous[i] for i in test)
    return {
        "data": {
            "file": path.name,
            "rows": len(t), "skipped": t.skipped, "users": t.users, "lexemes": t.lexemes,
            "train_rows": len(train), "test_rows": len(test),
            "train_share": train_share, "cutoff_timestamp": cutoff_ts,
            "first_timestamp": t.timestamp[order[0]] if order else None,
            "last_timestamp": t.timestamp[order[-1]] if order else None,
            "test_rows_with_previous_session": test_prev,
            "test_mean_delta_days": sum(t.delta_days[i] for i in test) / len(test) if test else None,
        },
        "models": results,
        "seconds_total": round(time.monotonic() - started, 1),
    }


NAMES = {
    "constant": "Константа (средняя доля верных на обучении)",
    "hlr_bias": "Экспонента с одним периодом полураспада для всех",
    "logistic": "Логистическая регрессия (те же признаки + log(1+Δ))",
    "hlr_ours": "**Наш планировщик** (HLR, признаки AdaptiveScheduler)",
}


def _ts(ts: int | None) -> str:
    return "—" if ts is None else time.strftime("%Y-%m-%d %H:%M UTC", time.gmtime(ts))


def render_markdown(res: dict) -> str:
    d = res["data"]
    lines = [
        "# Модель планировщика на данных Duolingo",
        "",
        "Данные: B. Settles, B. Meeder. *A Trainable Spaced Repetition Model for Language Learning*, ACL 2016; "
        "файл `settles.acl16.learning_traces.13m.csv.gz` (Harvard Dataverse), лицензия CC BY-NC 4.0. "
        "Ниже только агрегаты, посчитанные скриптом `srs/srs_sim/duolingo.py`; сами данные не публикуются.",
        "",
        f"- Строк: {d['rows']:,} (пропущено некорректных: {d['skipped']:,}), пользователей: {d['users']:,}, слов: {d['lexemes']:,}",
        f"- Период: {_ts(d['first_timestamp'])} — {_ts(d['last_timestamp'])}",
        f"- Обучение: первые {d['train_share']:.0%} строк по времени ({d['train_rows']:,}), "
        f"проверка: строки с {_ts(d['cutoff_timestamp'])} ({d['test_rows']:,})",
        f"- В проверке у {d['test_rows_with_previous_session']:,} строк есть прошлая сессия той же пары в данных "
        f"(для остальных признак «последний раз ошибка» = 0)",
        "",
        "| Модель | MAE ↓ | AUC ↑ | ECE ↓ | Среднее предсказание | Средняя доля верных |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for name, m in res["models"].items():
        auc = "—" if m["auc"] is None else f"{m['auc']:.4f}"
        lines.append(f"| {NAMES.get(name, name)} | {m['mae']:.4f} | {auc} | {m['ece']:.4f} "
                     f"| {m['mean_predicted']:.3f} | {m['mean_observed']:.3f} |")
    lines += [
        "",
        "- MAE — средняя |p̂ − доля верных в сессии| по строкам проверки.",
        "- AUC — по отдельным ответам: каждая строка даёт session_correct верных и session_seen − session_correct "
        "неверных ответов с одной оценкой p̂. 0.5 — не лучше случайного.",
        "- ECE — средняя по ответам |p̂ − доля верных| в 10 корзинах по p̂.",
        "",
        "## Калибровка нашей модели",
        "",
        "| p̂ | Строк | Ответов | Среднее p̂ | Доля верных |",
        "|---|---:|---:|---:|---:|",
    ]
    for c in res["models"]["hlr_ours"]["calibration"]:
        lines.append(f"| {c['bin']} | {c['rows']:,} | {c['answers']:,} | {c['predicted']:.3f} | {c['observed']:.3f} |")
    w = res["models"]["hlr_ours"]["weights"]
    lines += [
        "",
        f"Веса нашей модели после обучения: bias {w[0]}, √верных {w[1]}, √ошибок {w[2]}, последний раз ошибка {w[3]}.",
        "",
        f"Время прогона: {res['seconds_total']:.0f} с.",
        "",
    ]
    return "\n".join(lines)


def main() -> None:
    ap = argparse.ArgumentParser(description="Модель планировщика на данных Duolingo (Settles & Meeder, 2016)")
    ap.add_argument("--data", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--train-share", type=float, default=0.8)
    ap.add_argument("--limit", type=int, help="только первые N строк файла (для пробы)")
    args = ap.parse_args()
    res = run(args.data, args.train_share, args.limit, log=lambda s: print(s, flush=True))
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "metrics.json").write_text(json.dumps(res, ensure_ascii=False, indent=1), encoding="utf-8")
    (args.out / "results.md").write_text(render_markdown(res), encoding="utf-8")
    print(f"записано в {args.out}")


if __name__ == "__main__":
    main()
