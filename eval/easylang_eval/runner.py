"""Прогон: ответы трёх вариантов -> вердикты судьи -> отчёт.

Всё складывается в папку прогона:
  run.json         — настройки (без ключей)
  answers.jsonl    — ответы вариантов, по строке на (фраза, вариант)
  judgments.jsonl  — вердикты судьи
  results.md       — сводка метрик
  results.csv      — все ответы рядом с правильным смыслом, для ручной проверки

Прогон можно прервать и запустить снова с той же папкой: готовые ответы
и вердикты не запрашиваются повторно, ошибки сервиса — запрашиваются.
"""

from __future__ import annotations

import csv
import hashlib
import json
import threading
from concurrent.futures import ThreadPoolExecutor
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable

from .dataset import DEFAULT_PATH, Item
from .explainers import VARIANTS, Answer, DictionaryExplainer, LlmConfig, LlmExplainer
from .http_client import HttpClient
from .judge import LlmJudge, options_for
from .report import render_report
from .metrics import Row


@dataclass
class RunConfig:
    out_dir: Path
    llm: LlmConfig | None
    judge: LlmConfig | None
    variants: tuple[str, ...] = VARIANTS
    dictionary_url: str = "https://api.dictionaryapi.dev/api/v2/entries/en/"
    language: str = "Russian"
    workers: int = 1
    timeout: float = 60.0
    retries: int = 3
    backoff: float = 2.0
    dataset_path: Path = DEFAULT_PATH

    def public(self) -> dict:
        """Для run.json и отчёта: всё, кроме ключей."""
        def strip(cfg: LlmConfig | None):
            if cfg is None:
                return None
            d = asdict(cfg)
            d["api_key"] = "***" if cfg.api_key else ""
            return d

        return {
            "llm": strip(self.llm),
            "judge": strip(self.judge),
            "variants": list(self.variants),
            "dictionary_url": self.dictionary_url,
            "language": self.language,
            "dataset": str(self.dataset_path.name),
            "dataset_sha256": sha256(self.dataset_path),
        }


class ConfigMismatch(RuntimeError):
    pass


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()[:16]


def read_jsonl(path: Path) -> dict[tuple[str, str], dict]:
    """Последняя запись по ключу (id, variant) побеждает."""
    out: dict[tuple[str, str], dict] = {}
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.strip():
                rec = json.loads(line)
                out[(rec["id"], rec["variant"])] = rec
    return out


@dataclass
class JsonlWriter:
    path: Path
    lock: threading.Lock = field(default_factory=threading.Lock)

    def write(self, rec: dict) -> None:
        with self.lock, open(self.path, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec, ensure_ascii=False) + "\n")


def _check_config(out_dir: Path, public: dict, fresh: bool) -> None:
    path = out_dir / "run.json"
    if fresh:
        for name in ("answers.jsonl", "judgments.jsonl", "results.md", "results.csv"):
            (out_dir / name).unlink(missing_ok=True)
    elif path.exists():
        old = json.loads(path.read_text(encoding="utf-8"))
        keys = ("llm", "judge", "dictionary_url", "language", "dataset_sha256")
        changed = [k for k in keys if old.get(k) != public.get(k)]
        if changed:
            raise ConfigMismatch(
                f"в {out_dir} уже есть прогон с другими настройками ({', '.join(changed)}). "
                "Укажите другую папку --out или запустите с --fresh."
            )
    path.write_text(json.dumps(public, ensure_ascii=False, indent=2), encoding="utf-8")


def run(items: list[Item], cfg: RunConfig, fresh: bool = False, log: Callable[[str], None] = print) -> Path:
    if cfg.judge is None:
        raise ValueError("нужен судья: задайте --model/--base-url или --judge-*")
    if any(v != "dictionary" for v in cfg.variants) and cfg.llm is None:
        raise ValueError("для вариантов llm_* нужна модель: --base-url, --model")

    cfg.out_dir.mkdir(parents=True, exist_ok=True)
    public = cfg.public()
    _check_config(cfg.out_dir, public, fresh)

    http = HttpClient(timeout=cfg.timeout, retries=cfg.retries, backoff=cfg.backoff)
    explainers = {}
    for v in cfg.variants:
        if v == "dictionary":
            explainers[v] = DictionaryExplainer(http, cfg.dictionary_url)
        elif v in ("llm_plain", "llm_context"):
            explainers[v] = LlmExplainer(http, cfg.llm, use_context=(v == "llm_context"), language=cfg.language)
        else:
            raise ValueError(f"неизвестный вариант {v!r}, допустимы: {', '.join(VARIANTS)}")
    judge = LlmJudge(http, cfg.judge)

    answers_path, judgments_path = cfg.out_dir / "answers.jsonl", cfg.out_dir / "judgments.jsonl"
    answers = read_jsonl(answers_path)
    judgments = read_jsonl(judgments_path)

    # 1. Ответы вариантов.
    todo = [(it, v) for it in items for v in cfg.variants if not answers.get((it.id, v), {}).get("ok")]
    log(f"ответы: {len(items) * len(cfg.variants) - len(todo)} готово, {len(todo)} запросить")
    writer = JsonlWriter(answers_path)
    counter = _Counter(len(todo))

    def do_answer(task):
        it, v = task
        a: Answer = explainers[v].explain(it)
        rec = {"id": it.id, "variant": v, **asdict(a)}
        writer.write(rec)
        answers[(it.id, v)] = rec
        log(f"[{counter.next()}/{len(todo)}] {v:<11} {it.id} " + ("ok" if a.ok else f"ОШИБКА: {a.error}"))

    _map(do_answer, todo, cfg.workers)

    # 2. Вердикты судьи для успешных ответов. Если ответ изменился — вердикт устарел.
    def needs_judgment(it: Item, v: str) -> bool:
        a = answers.get((it.id, v))
        j = judgments.get((it.id, v))
        return bool(a and a["ok"]) and not (j and j["ok"] and j["answer_text"] == a["text"])

    todo_j = [(it, v) for it in items for v in cfg.variants if needs_judgment(it, v)]
    log(f"судья: {len(todo_j)} вердиктов запросить")
    jwriter = JsonlWriter(judgments_path)
    jcounter = _Counter(len(todo_j))

    def do_judge(task):
        it, v = task
        text = answers[(it.id, v)]["text"]
        verdict = judge.judge(it, text)
        rec = {"id": it.id, "variant": v, "answer_text": text, **asdict(verdict)}
        jwriter.write(rec)
        judgments[(it.id, v)] = rec
        log(f"[{jcounter.next()}/{len(todo_j)}] судья {v:<11} {it.id} " + (verdict.kind if verdict.ok else f"ОШИБКА: {verdict.error}"))

    _map(do_judge, todo_j, cfg.workers)

    return write_report(items, cfg.out_dir, public, cfg.variants)


def merge_runs(items: list[Item], parts: list[Path], into: Path) -> Path:
    """Собрать части одного прогона (одинаковые настройки) в папку into и построить отчёт."""
    configs = [json.loads((p / "run.json").read_text(encoding="utf-8")) for p in parts]
    keys = ("llm", "judge", "dictionary_url", "language", "dataset_sha256", "variants")
    for p, c in zip(parts[1:], configs[1:]):
        changed = [k for k in keys if c.get(k) != configs[0].get(k)]
        if changed:
            raise ConfigMismatch(f"{p} прогнан с другими настройками ({', '.join(changed)}), чем {parts[0]}")
    into.mkdir(parents=True, exist_ok=True)
    (into / "run.json").write_text(json.dumps(configs[0], ensure_ascii=False, indent=2), encoding="utf-8")
    for name in ("answers.jsonl", "judgments.jsonl"):
        records: dict[tuple[str, str], dict] = {}
        for p in parts:
            records.update(read_jsonl(p / name))
        with open(into / name, "w", encoding="utf-8") as f:
            for rec in records.values():
                f.write(json.dumps(rec, ensure_ascii=False) + "\n")
    return write_report(items, into, configs[0])


def write_report(items: list[Item], out_dir: Path, public: dict | None = None,
                 variants: tuple[str, ...] | None = None) -> Path:
    if public is None:
        public = json.loads((out_dir / "run.json").read_text(encoding="utf-8"))
    variants = tuple(variants or public["variants"])
    answers = read_jsonl(out_dir / "answers.jsonl")
    judgments = read_jsonl(out_dir / "judgments.jsonl")

    rows, errors = [], []
    for it in items:
        for v in variants:
            a, j = answers.get((it.id, v)), judgments.get((it.id, v))
            answered = bool(a and a["ok"])
            judged = bool(answered and j and j["ok"] and j["answer_text"] == a["text"])
            kind = j["kind"] if judged else "error"
            rows.append(Row(it.id, it.category, v, answered, judged, kind, bool(a and a.get("found"))))
            if a and not a["ok"]:
                errors.append(f"{v} {it.id}: {a['error']}")
            elif answered and j and not j["ok"]:
                errors.append(f"судья {v} {it.id}: {j['error']}")

    meta = {**public, "generated_at": datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC"), "items": len(items)}
    path = out_dir / "results.md"
    path.write_text(render_report(rows, variants, meta, errors), encoding="utf-8")
    _write_csv(items, variants, answers, judgments, out_dir / "results.csv")
    return path


def _write_csv(items, variants, answers, judgments, path: Path) -> None:
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["id", "category", "phrase", "line", "variant", "answer", "judge_choice", "verdict",
                    "correct_meaning", "manual_ok"])
        for it in items:
            opts = options_for(it)
            for v in variants:
                a, j = answers.get((it.id, v)), judgments.get((it.id, v))
                choice = j.get("choice") if j and j.get("ok") else None
                w.writerow([
                    it.id, it.category, it.phrase, it.line, v,
                    (a or {}).get("text", "") if a and a["ok"] else f"ОШИБКА: {(a or {}).get('error', 'нет ответа')}",
                    opts.texts[choice] if choice is not None else "",
                    j["kind"] if j and j.get("ok") else "",
                    it.correct,
                    "",
                ])


class _Counter:
    def __init__(self, total: int) -> None:
        self.n, self.total, self.lock = 0, total, threading.Lock()

    def next(self) -> int:
        with self.lock:
            self.n += 1
            return self.n


def _map(fn, tasks, workers: int) -> None:
    if workers <= 1:
        for t in tasks:
            fn(t)
        return
    with ThreadPoolExecutor(max_workers=workers) as pool:
        list(pool.map(fn, tasks))
