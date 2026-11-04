"""Набор фраз для оценки: загрузка и проверка формата."""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path

CATEGORIES = ("slang", "polysemy", "control")
DEFAULT_PATH = Path(__file__).resolve().parent.parent / "data" / "phrases.jsonl"

# То же, что PhraseNormalizer.Tokenize в основном приложении:
# слово — буквы и цифры, внутри могут быть апострофы (don't, rock'n'roll).
_WORD = re.compile(r"[^\W_]+(?:'[^\W_]+)*")


def tokenize(text: str) -> list[str]:
    unified = text.lower().replace("’", "'").replace("‘", "'")
    return _WORD.findall(unified)


def contains_phrase(text: str, phrase: str) -> bool:
    t, p = tokenize(text), tokenize(phrase)
    return bool(p) and any(t[i: i + len(p)] == p for i in range(len(t) - len(p) + 1))


@dataclass(frozen=True)
class Item:
    id: str
    category: str
    phrase: str
    title: str
    speaker: str
    before: tuple[str, ...]
    line: str
    after: tuple[str, ...]
    correct: str
    distractors: tuple[str, ...]


class DatasetError(ValueError):
    pass


def parse_item(raw: dict, where: str) -> Item:
    try:
        item = Item(
            id=raw["id"],
            category=raw["category"],
            phrase=raw["phrase"],
            title=raw["title"],
            speaker=raw["speaker"],
            before=tuple(raw["before"]),
            line=raw["line"],
            after=tuple(raw["after"]),
            correct=raw["correct"],
            distractors=tuple(raw["distractors"]),
        )
    except KeyError as e:
        raise DatasetError(f"{where}: нет поля {e}") from None
    problems = validate_item(item)
    if problems:
        raise DatasetError(f"{where} ({item.id}): " + "; ".join(problems))
    return item


def validate_item(item: Item) -> list[str]:
    problems = []
    if item.category not in CATEGORIES:
        problems.append(f"неизвестная категория {item.category!r}")
    if len(item.before) != 2 or len(item.after) != 2:
        problems.append("нужно ровно две реплики до и две после")
    if not contains_phrase(item.line, item.phrase):
        problems.append(f"фразы {item.phrase!r} нет в реплике")
    if not item.correct.strip():
        problems.append("пустой правильный смысл")
    if not 1 <= len(item.distractors) <= 3:
        problems.append("нужно от 1 до 3 неверных смыслов")
    if item.correct in item.distractors or len(set(item.distractors)) != len(item.distractors):
        problems.append("неверные смыслы повторяются или совпадают с правильным")
    return problems


def load(path: Path = DEFAULT_PATH) -> list[Item]:
    items: list[Item] = []
    seen: set[str] = set()
    with open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            if not line.strip():
                continue
            where = f"{path.name}:{n}"
            try:
                raw = json.loads(line)
            except json.JSONDecodeError as e:
                raise DatasetError(f"{where}: битый JSON ({e.msg})") from None
            item = parse_item(raw, where)
            if item.id in seen:
                raise DatasetError(f"{where}: повторяется id {item.id}")
            seen.add(item.id)
            items.append(item)
    return items
