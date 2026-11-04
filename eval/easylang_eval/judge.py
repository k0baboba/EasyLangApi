"""Автоматическая проверка ответа: LLM-судья выбирает смысл из списка.

Судья видит фразу, текст объяснения и пронумерованный список: правильный смысл,
неверные смыслы из набора и вариант «ни один не подходит». Порядок перемешан,
но зависит только от id фразы, поэтому одинаков для всех трёх вариантов.
Реплику и контекст судья не видит — иначе он мог бы «додумать» правильный
смысл за плохое объяснение. Какой вариант дал объяснение, судья тоже не знает.
"""

from __future__ import annotations

import json
import random
import re
from dataclasses import dataclass

from .dataset import Item
from .explainers import LlmConfig, chat
from .http_client import HttpClient, ServiceError

NONE_OPTION = "Ни один из перечисленных смыслов (объяснение пустое, неверное или не выбирает смысл)"

JUDGE_SYSTEM_PROMPT = (
    "You grade explanations produced by a language-learning tool.\n"
    "You get an English phrase, an explanation of it, and numbered candidate meanings (in Russian).\n"
    "Pick the single candidate that matches what the explanation says the phrase means.\n"
    "Judge only the explanation text: do not use your own knowledge of the phrase to fix a wrong or vague explanation.\n"
    "If the explanation is empty, matches none of the candidates, or lists several unrelated meanings "
    "without committing to one, pick the last candidate.\n"
    'Reply with one JSON object and nothing else: {"choice": <number>}'
)


@dataclass(frozen=True)
class Options:
    texts: tuple[str, ...]   # нумерация с 1 в промпте
    correct: int             # индексы с 0
    none: int

    def kind(self, choice: int | None) -> str:
        if choice is None:
            return "unparsed"
        if choice == self.correct:
            return "correct"
        if choice == self.none:
            return "none"
        return "distractor"


def options_for(item: Item) -> Options:
    meanings = [item.correct, *item.distractors]
    random.Random(f"options-{item.id}").shuffle(meanings)
    texts = (*meanings, NONE_OPTION)
    return Options(texts=texts, correct=meanings.index(item.correct), none=len(texts) - 1)


def build_judge_prompt(item: Item, explanation: str, options: Options) -> str:
    lines = [
        f'Phrase: "{item.phrase}"',
        "Explanation:",
        explanation.strip() or "(empty)",
        "",
        "Candidates:",
    ]
    lines += [f"{i + 1}. {t}" for i, t in enumerate(options.texts)]
    return "\n".join(lines)


_NUMBER = re.compile(r"\d+")


def parse_choice(content: str, n_options: int) -> int | None:
    """Номер варианта (с 1) из ответа судьи -> индекс с 0. None — не разобрали."""
    start, end = content.find("{"), content.rfind("}")
    value = None
    if start >= 0 and end > start:
        try:
            obj = json.loads(content[start: end + 1])
            value = obj.get("choice") if isinstance(obj, dict) else None
        except json.JSONDecodeError:
            pass
    if value is None:
        m = _NUMBER.search(content)
        value = m.group() if m else None
    try:
        n = int(value)
    except (TypeError, ValueError):
        return None
    return n - 1 if 1 <= n <= n_options else None


@dataclass(frozen=True)
class Verdict:
    ok: bool
    choice: int | None = None
    kind: str = ""           # correct / distractor / none / unparsed
    raw: str = ""
    error: str | None = None


class LlmJudge:
    def __init__(self, http: HttpClient, cfg: LlmConfig) -> None:
        self.http, self.cfg = http, cfg

    def judge(self, item: Item, explanation: str) -> Verdict:
        options = options_for(item)
        if not explanation.strip():
            # Пустой ответ не нужно отправлять судье: это всегда «ни один».
            return Verdict(ok=True, choice=options.none, kind="none", raw="(пустое объяснение, судья не вызывался)")
        messages = [
            {"role": "system", "content": JUDGE_SYSTEM_PROMPT},
            {"role": "user", "content": build_judge_prompt(item, explanation, options)},
        ]
        try:
            content = chat(self.http, self.cfg, messages, temperature=0.0)
        except ServiceError as e:
            return Verdict(ok=False, error=str(e))
        choice = parse_choice(content, len(options.texts))
        return Verdict(ok=True, choice=choice, kind=options.kind(choice), raw=content)
