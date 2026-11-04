"""Три сравниваемых варианта разбора фразы.

* dictionary  — как DictionaryExplainer: dictionaryapi.dev, контекст только сортирует значения;
* llm_plain   — LLM с тем же промптом, что в приложении, но без контекста (PhraseContext.Empty);
* llm_context — LLM с контекстом, как в приложении: название, говорящий, 2 реплики до, реплика, 2 после.
"""

from __future__ import annotations

import time
import urllib.parse
from dataclasses import dataclass, replace

from .dataset import Item, tokenize
from .http_client import HttpClient, ServiceError, join_url
from .prompts import EMPTY_CONTEXT, PhraseContext, build_system_prompt, build_user_prompt, parse_llm_answer

VARIANTS = ("dictionary", "llm_plain", "llm_context")


@dataclass(frozen=True)
class Answer:
    """Результат одного разбора. ok=False — сбой сервиса, а не неверный ответ."""

    ok: bool
    text: str = ""          # что увидит пользователь и что оценивает судья
    found: bool = False     # источник считает, что знает фразу
    raw: str = ""
    error: str | None = None
    elapsed_ms: int = 0


@dataclass(frozen=True)
class LlmConfig:
    base_url: str
    api_key: str
    model: str
    json_mode: bool = True
    temperature: float = 0.2      # как в LlmExplainer
    seed: int | None = None       # OpenAI и Ollama его понимают; детерминизм не гарантирован


def context_of(item: Item) -> PhraseContext:
    return PhraseContext(
        title=item.title,
        speaker=item.speaker,
        before=item.before,
        current_line=item.line,
        after=item.after,
    )


def chat(http: HttpClient, cfg: LlmConfig, messages: list[dict], temperature: float | None = None) -> str:
    """Один запрос Chat Completions. Возвращает content первого варианта ответа."""
    body: dict = {
        "model": cfg.model,
        "messages": messages,
        "temperature": cfg.temperature if temperature is None else temperature,
    }
    if cfg.json_mode:
        body["response_format"] = {"type": "json_object"}
    if cfg.seed is not None:
        body["seed"] = cfg.seed
    url = join_url(cfg.base_url, "chat/completions")
    try:
        # Ollama ключ не проверяет, поэтому без ключа заголовок просто не отправляем.
        headers = {"Authorization": f"Bearer {cfg.api_key}"} if cfg.api_key else {}
        resp = http.request("POST", url, body, headers)
    except ServiceError as e:
        raise ServiceError(_describe(e), e.status, e.body) from None
    if resp.status == 404:
        raise ServiceError(f"{url}: 404 — проверьте адрес и название модели ({cfg.model})", 404, resp.body)
    try:
        content = resp.json()["choices"][0]["message"]["content"]
    except (ValueError, KeyError, IndexError, TypeError):
        raise ServiceError(f"неожиданный формат ответа: {resp.body[:300]}") from None
    if not isinstance(content, str) or not content.strip():
        raise ServiceError("модель вернула пустой ответ")
    return content


def _describe(e: ServiceError) -> str:
    if e.status in (401, 403):
        return f"ключ отклонён ({e.status}): проверьте --api-key"
    if e.status == 400 and "response_format" in e.body:
        return f"{e} — сервис не понимает response_format, запустите с --no-json-mode"
    return str(e)


class LlmExplainer:
    def __init__(self, http: HttpClient, cfg: LlmConfig, use_context: bool, language: str = "Russian") -> None:
        self.http, self.cfg, self.use_context, self.language = http, cfg, use_context, language

    def messages(self, item: Item) -> list[dict]:
        ctx = context_of(item) if self.use_context else EMPTY_CONTEXT
        return [
            {"role": "system", "content": build_system_prompt(self.language)},
            {"role": "user", "content": build_user_prompt(item.phrase, ctx)},
        ]

    def explain(self, item: Item) -> Answer:
        start = time.monotonic()
        try:
            content = chat(self.http, self.cfg, self.messages(item))
        except ServiceError as e:
            return Answer(ok=False, error=str(e), elapsed_ms=_ms(start))
        parsed = parse_llm_answer(content)
        return Answer(ok=True, text=parsed.text_for_judge(), found=parsed.found, raw=content, elapsed_ms=_ms(start))


class DictionaryExplainer:
    """Повторяет логику DictionaryExplainer.cs, чтобы сравнение было с тем, что делает приложение."""

    MAX_SENSES = 6
    DEFINITIONS_PER_MEANING = 2
    MAX_WORDS_IN_FALLBACK = 4

    def __init__(self, http: HttpClient, base_url: str = "https://api.dictionaryapi.dev/api/v2/entries/en/") -> None:
        self.http, self.base_url = http, base_url

    def fetch(self, term: str, server_error_means_not_found: bool = False) -> list | None:
        """None — словарь не знает слова (404, а при server_error_means_not_found — и 5xx)."""
        url = join_url(self.base_url, urllib.parse.quote(term, safe=""))
        try:
            # 5xx на выражение — ответ по существу, а не сбой: повторять его незачем.
            http = replace(self.http, retries=0) if server_error_means_not_found else self.http
            resp = http.request("GET", url)
        except ServiceError as e:
            if server_error_means_not_found and e.status is not None and e.status >= 500:
                return None
            raise
        if resp.status == 404:
            return None
        try:
            data = resp.json()
        except ValueError:
            raise ServiceError(f"словарь вернул не JSON: {resp.body[:200]}") from None
        return data if isinstance(data, list) else None

    @classmethod
    def to_senses(cls, word: str, entries: list) -> list[dict]:
        senses = []
        for entry in entries:
            for meaning in (entry or {}).get("meanings") or []:
                defs = [d for d in meaning.get("definitions") or [] if (d.get("definition") or "").strip()]
                for d in defs[: cls.DEFINITIONS_PER_MEANING]:
                    senses.append({
                        "word": word,
                        "partOfSpeech": meaning.get("partOfSpeech"),
                        "definition": d["definition"],
                        "example": d.get("example"),
                    })
        return senses

    @staticmethod
    def rank_by_context(senses: list[dict], ctx: PhraseContext) -> list[dict]:
        parts = [ctx.title, ctx.current_line, *ctx.before, *ctx.after]
        context_words = {w for w in tokenize(" ".join(p for p in parts if p and p.strip())) if len(w) > 3}
        if not context_words:
            return senses

        def overlap(s: dict) -> int:
            return len(set(tokenize(f"{s['definition']} {s.get('example') or ''}")) & context_words)

        return sorted(senses, key=overlap, reverse=True)   # sorted стабилен, как OrderByDescending

    def explain(self, item: Item) -> Answer:
        start = time.monotonic()
        words = list(dict.fromkeys(tokenize(item.phrase)))
        is_multi_word = len(words) > 1
        try:
            # Как в приложении (3f81371): на выражение из нескольких слов словарь иногда
            # отвечает 5xx — это значит то же, что 404, переходим к отдельным словам.
            entries = self.fetch(item.phrase, server_error_means_not_found=is_multi_word)
            senses = self.to_senses(item.phrase, entries) if entries else []
            found_as_whole = bool(senses)
            if not found_as_whole:
                if is_multi_word:
                    for w in [w for w in words if len(w) > 2][: self.MAX_WORDS_IN_FALLBACK]:
                        we = self.fetch(w)
                        if we:
                            senses.extend(self.to_senses(w, we)[: self.DEFINITIONS_PER_MEANING])
        except ServiceError as e:
            return Answer(ok=False, error=str(e), elapsed_ms=_ms(start))

        ranked = self.rank_by_context(senses, context_of(item))[: self.MAX_SENSES]
        # Судья оценивает первое значение — его пользователь видит первым (Summary в приложении).
        # Если фраза не найдена целиком, приложение показывает значения отдельных слов без Summary.
        if not ranked:
            text = ""
        elif found_as_whole:
            text = ranked[0]["definition"]
        else:
            text = f"{ranked[0]['word']}: {ranked[0]['definition']}"
        raw = "\n".join(f"{s['word']} ({s.get('partOfSpeech')}): {s['definition']}" for s in ranked)
        return Answer(ok=True, text=text, found=found_as_whole, raw=raw, elapsed_ms=_ms(start))


def _ms(start: float) -> int:
    return int((time.monotonic() - start) * 1000)
