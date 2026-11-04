"""python -m easylang_eval run ...  |  python -m easylang_eval report --out DIR"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

from .dataset import CATEGORIES, DEFAULT_PATH, DatasetError, load
from .explainers import VARIANTS, LlmConfig
from .runner import ConfigMismatch, RunConfig, run, write_report

OUT_ROOT = Path(__file__).resolve().parent.parent / "out"


def env(name: str, default: str | None = None) -> str | None:
    v = os.environ.get(name)
    return v if v not in (None, "") else default


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="python -m easylang_eval",
                                description="Сравнение разбора фраз: словарь, LLM без контекста, LLM с контекстом")
    sub = p.add_subparsers(dest="command", required=True)

    r = sub.add_parser("run", help="прогнать варианты и судью, записать отчёт")
    g = r.add_argument_group("модель разбора (любой OpenAI-совместимый API)")
    g.add_argument("--base-url", default=env("EVAL_BASE_URL"),
                   help="например https://api.openai.com/v1 или http://localhost:11434/v1 [EVAL_BASE_URL]")
    g.add_argument("--api-key", default=env("EVAL_API_KEY", ""),
                   help="ключ; лучше через переменную EVAL_API_KEY, чтобы не попал в историю. Ollama ключ не проверяет")
    g.add_argument("--model", default=env("EVAL_MODEL"), help="название модели [EVAL_MODEL]")
    g.add_argument("--no-json-mode", action="store_true",
                   help="не отправлять response_format (если сервис его не понимает)")
    g.add_argument("--seed", type=int, default=None, help="seed для API, если поддерживается")

    j = r.add_argument_group("судья (по умолчанию та же модель)")
    j.add_argument("--judge-base-url", default=env("EVAL_JUDGE_BASE_URL"))
    j.add_argument("--judge-api-key", default=env("EVAL_JUDGE_API_KEY"))
    j.add_argument("--judge-model", default=env("EVAL_JUDGE_MODEL"))

    o = r.add_argument_group("прогон")
    o.add_argument("--dictionary-url", default=env("EVAL_DICTIONARY_URL", "https://api.dictionaryapi.dev/api/v2/entries/en/"))
    o.add_argument("--variants", default=",".join(VARIANTS), help=f"через запятую из: {', '.join(VARIANTS)}")
    o.add_argument("--category", choices=CATEGORIES, help="только одна категория")
    o.add_argument("--ids", help="только эти id через запятую")
    o.add_argument("--limit", type=int, help="только первые N фраз (для пробного запуска)")
    o.add_argument("--language", default="Russian", help="язык объяснений, как Llm:ExplanationLanguage")
    o.add_argument("--workers", type=int, default=1, help="параллельных запросов (для Ollama оставьте 1)")
    o.add_argument("--timeout", type=float, default=60.0, help="таймаут запроса, секунд")
    o.add_argument("--retries", type=int, default=3, help="повторов при 429/5xx и обрыве соединения")
    o.add_argument("--dataset", type=Path, default=DEFAULT_PATH)
    o.add_argument("--out", type=Path, help="папка прогона (по умолчанию out/<модель>)")
    o.add_argument("--fresh", action="store_true", help="начать заново, удалив прежние ответы в папке")

    rep = sub.add_parser("report", help="пересчитать results.md по готовым файлам прогона")
    rep.add_argument("--out", type=Path, required=True)
    rep.add_argument("--dataset", type=Path, default=DEFAULT_PATH)
    return p


def select(items, args):
    if args.category:
        items = [i for i in items if i.category == args.category]
    if args.ids:
        wanted = {s.strip() for s in args.ids.split(",") if s.strip()}
        unknown = wanted - {i.id for i in items}
        if unknown:
            raise SystemExit(f"нет таких id: {', '.join(sorted(unknown))}")
        items = [i for i in items if i.id in wanted]
    if args.limit is not None:
        items = items[: args.limit]
    return items


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        items = load(args.dataset)
    except (DatasetError, OSError) as e:
        print(f"набор фраз не загрузился: {e}", file=sys.stderr)
        return 2

    if args.command == "report":
        print(write_report(items, args.out))
        return 0

    variants = tuple(v.strip() for v in args.variants.split(",") if v.strip())
    bad = [v for v in variants if v not in VARIANTS]
    if bad:
        print(f"неизвестные варианты: {', '.join(bad)}", file=sys.stderr)
        return 2

    llm = None
    if args.base_url and args.model:
        llm = LlmConfig(args.base_url, args.api_key or "", args.model, json_mode=not args.no_json_mode, seed=args.seed)
    judge = None
    jb, jm = args.judge_base_url or args.base_url, args.judge_model or args.model
    if jb and jm:
        jk = args.judge_api_key if args.judge_api_key is not None else (args.api_key or "")
        judge = LlmConfig(jb, jk, jm, json_mode=not args.no_json_mode, seed=args.seed)
    if judge is None:
        print("нужна модель: задайте --base-url и --model (или EVAL_BASE_URL и EVAL_MODEL)", file=sys.stderr)
        return 2

    out = args.out or OUT_ROOT / re.sub(r"[^\w.-]+", "_", (llm or judge).model)
    cfg = RunConfig(out_dir=out, llm=llm, judge=judge, variants=variants, dictionary_url=args.dictionary_url,
                    language=args.language, workers=args.workers, timeout=args.timeout, retries=args.retries,
                    dataset_path=args.dataset)
    try:
        path = run(select(items, args), cfg, fresh=args.fresh, log=lambda s: print(s, file=sys.stderr, flush=True))
    except (ConfigMismatch, ValueError) as e:
        print(e, file=sys.stderr)
        return 2
    print(path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
