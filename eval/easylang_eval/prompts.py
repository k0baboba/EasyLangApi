"""Копия PromptBuilder.cs и LlmResponseParser.cs из основного приложения.

Оценка должна мерить тот же промпт, что уходит из приложения. Если промпт
в Explaining/PromptBuilder.cs меняется, этот файл нужно обновить вместе с ним
(тест tests/test_prompts.py сверяет текст с исходником C#, если он рядом).
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field


@dataclass(frozen=True)
class PhraseContext:
    """Как PhraseContext в приложении: все поля необязательные."""

    title: str | None = None
    speaker: str | None = None
    before: tuple[str, ...] = field(default_factory=tuple)
    current_line: str | None = None
    after: tuple[str, ...] = field(default_factory=tuple)


EMPTY_CONTEXT = PhraseContext()


def build_system_prompt(explanation_language: str) -> str:
    lang = explanation_language
    return (
        f"You help a {lang}-speaking learner understand English from films, series and games.\n"
        "Explain the phrase as it is used in the given scene, not word by word:\n"
        "cover slang, idioms, register and tone when they matter.\n"
        "Reply with one JSON object and nothing else:\n"
        "{\n"
        f'  "translation": "natural translation of the phrase in this scene, in {lang}",\n'
        f'  "meaning": "what the phrase means here, one or two sentences in {lang}",\n'
        '  "partOfSpeech": "part of speech in English, or idiom / phrasal verb / slang",\n'
        f'  "contextNote": "why it is said this way in this scene, in {lang}, or null",\n'
        '  "example": "one more short English example with the same meaning"\n'
        "}"
    )


def _blank(s: str | None) -> bool:
    return s is None or not s.strip()


def build_user_prompt(phrase: str, context: PhraseContext) -> str:
    lines = [f'Phrase: "{phrase}"']
    has_context = False

    if not _blank(context.title):
        lines.append(f"Work: {context.title}")
        has_context = True
    if not _blank(context.speaker):
        lines.append(f"Speaker: {context.speaker}")
        has_context = True

    has_context |= _append_lines(lines, "Previous lines:", context.before)

    if not _blank(context.current_line):
        lines.append(f"Line with the phrase: {context.current_line}")
        has_context = True

    has_context |= _append_lines(lines, "Next lines:", context.after)

    if not has_context:
        lines.append("No context is available. Explain the most common meaning.")

    return "\n".join(lines).rstrip()


def _append_lines(lines: list[str], header: str, items) -> bool:
    non_empty = [s for s in (items or ()) if not _blank(s)]
    if not non_empty:
        return False
    lines.append(header)
    lines.extend(f"- {s}" for s in non_empty)
    return True


@dataclass(frozen=True)
class ParsedAnswer:
    found: bool
    summary: str | None
    translation: str | None = None
    context_note: str | None = None
    json_ok: bool = False

    def text_for_judge(self) -> str:
        """То, что пользователь видит первым: перевод и смысл."""
        if not self.json_ok:
            return self.summary or ""
        parts = []
        if self.translation:
            parts.append(f"Перевод: {self.translation}")
        if self.summary and self.summary != self.translation:
            parts.append(f"Смысл: {self.summary}")
        return "\n".join(parts)


def extract_json_object(content: str) -> str | None:
    start, end = content.find("{"), content.rfind("}")
    return content[start: end + 1] if start >= 0 and end > start else None


def parse_llm_answer(content: str) -> ParsedAnswer:
    """Как LlmResponseParser.Parse: первый {...}; не разобрался — весь текст в summary."""
    raw = extract_json_object(content)
    answer = None
    if raw is not None:
        try:
            obj = json.loads(raw)
            if isinstance(obj, dict):
                # JsonSerializerDefaults.Web: имена свойств без учёта регистра.
                answer = {str(k).lower(): v for k, v in obj.items()}
        except json.JSONDecodeError:
            pass

    def text(key: str) -> str | None:
        v = answer.get(key) if answer else None
        return v.strip() if isinstance(v, str) and v.strip() else None

    meaning, translation = text("meaning"), text("translation")
    if answer is None or (meaning is None and translation is None):
        t = content.strip()
        return ParsedAnswer(found=bool(t), summary=t or None)

    return ParsedAnswer(
        found=True,
        summary=meaning or translation,
        translation=translation,
        context_note=text("contextnote"),
        json_ok=True,
    )
