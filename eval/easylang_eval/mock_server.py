"""Подставной сервер для проверки скрипта без настоящей модели.

Отвечает как OpenAI-совместимый API (POST /v1/chat/completions) и как dictionaryapi.dev
(GET /api/v2/entries/en/{word}). Ответы строятся по жёстким правилам из набора фраз,
поэтому результат прогона известен заранее и ничего не говорит о качестве моделей:

* разбор с контекстом («Line with the phrase:» в промпте) — всегда правильный смысл;
* разбор без контекста — «самое частое» значение фразы: буквальное, если для фразы есть
  контрольная запись, иначе первый неверный смысл. Ответ зависит только от фразы;
* словарь знает только однословные фразы и первым ставит то же «самое частое» значение;
* судья выбирает вариант, текст которого целиком есть в объяснении, иначе «ни один».

Каждый третий ответ модели обёрнут в ```json с пояснением — проверка разбора «грязного» JSON.

Запуск отдельно:  python -m easylang_eval.mock_server --port 8089
"""

from __future__ import annotations

import argparse
import json
import re
import threading
from collections import defaultdict
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote

from .dataset import Item, load, tokenize

CHAT_PATH = "/v1/chat/completions"
DICT_PREFIX = "/api/v2/entries/en/"


class MockState:
    def __init__(self, items: list[Item], api_key: str | None = None, reject_json_mode: bool = False,
                 fail_every: int = 0) -> None:
        self.items = items
        self.api_key = api_key
        self.reject_json_mode = reject_json_mode
        self.fail_every = fail_every
        self.requests: list[dict] = []
        self.lock = threading.Lock()
        self.count = 0
        self.by_line = {(i.phrase.lower(), i.line): i for i in items}
        self.by_phrase: dict[str, list[Item]] = defaultdict(list)
        for i in items:
            self.by_phrase[i.phrase.lower()].append(i)

    def next_number(self) -> int:
        with self.lock:
            self.count += 1
            return self.count

    # --- словарь ---

    def literal_meaning(self, phrase: str) -> str | None:
        """«Самое частое» значение фразы: буквальное из контрольной записи или первый неверный смысл."""
        group = self.by_phrase.get(phrase.lower())
        if not group:
            return None
        controls = [i for i in group if i.category == "control"]
        return controls[0].correct if controls else group[0].distractors[0]

    def dictionary_senses(self, word: str) -> list[str] | None:
        group = self.by_phrase.get(word.lower())
        if not group or len(tokenize(word)) != 1:
            return None
        literal = self.literal_meaning(word)
        figurative = [i.correct for i in group if i.category != "control"]
        return [literal, *[f for f in figurative if f != literal]]

    # --- модель ---

    def explain(self, user_prompt: str, n: int) -> str:
        m = re.match(r'Phrase: "(.*)"', user_prompt)
        phrase = m.group(1).lower() if m else ""
        line = re.search(r"^Line with the phrase: (.*)$", user_prompt, re.M)
        if line:
            item = self.by_line.get((phrase, line.group(1)))
            meaning = item.correct if item else "неизвестно"
        else:
            # Без контекста ответ может зависеть только от самой фразы.
            meaning = self.literal_meaning(phrase) or "неизвестно"
        body = json.dumps({"translation": meaning, "meaning": meaning, "partOfSpeech": "slang",
                           "contextNote": None, "example": "Mock example."}, ensure_ascii=False)
        return f"Вот разбор:\n```json\n{body}\n```" if n % 3 == 0 else body

    @staticmethod
    def judge(user_prompt: str) -> str:
        explanation = user_prompt.split("Explanation:\n", 1)[1].split("\n\nCandidates:\n", 1)[0]
        candidates = re.findall(r"^(\d+)\. (.*)$", user_prompt.split("Candidates:\n", 1)[1], re.M)
        choice = next((int(n) for n, text in candidates if text in explanation), int(candidates[-1][0]))
        return json.dumps({"choice": choice})


def make_handler(state: MockState):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):  # тише в тестах
            pass

        def send_json(self, status: int, obj) -> None:
            data = json.dumps(obj, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            if not self.path.startswith(DICT_PREFIX):
                return self.send_json(404, {"error": "not found"})
            word = unquote(self.path[len(DICT_PREFIX):])
            state.requests.append({"method": "GET", "path": self.path})
            senses = state.dictionary_senses(word)
            if senses is None:
                return self.send_json(404, {"title": "No Definitions Found"})
            self.send_json(200, [{
                "word": word,
                "phonetic": "/mɒk/",
                "meanings": [{"partOfSpeech": "noun", "definitions": [{"definition": s} for s in senses]}],
            }])

        def do_POST(self):
            length = int(self.headers.get("Content-Length") or 0)
            body = json.loads(self.rfile.read(length) or b"{}")
            n = state.next_number()
            state.requests.append({"method": "POST", "path": self.path, "body": body,
                                   "authorization": self.headers.get("Authorization")})
            if self.path != CHAT_PATH:
                return self.send_json(404, {"error": {"message": f"unknown path {self.path}"}})
            if state.api_key is not None and self.headers.get("Authorization") != f"Bearer {state.api_key}":
                return self.send_json(401, {"error": {"message": "invalid api key"}})
            if state.reject_json_mode and "response_format" in body:
                return self.send_json(400, {"error": {"message": "unknown field: response_format"}})
            if state.fail_every and n % state.fail_every == 0:
                return self.send_json(503, {"error": {"message": "temporarily overloaded"}})

            system, user = body["messages"][0]["content"], body["messages"][-1]["content"]
            content = state.judge(user) if system.startswith("You grade") else state.explain(user, n)
            self.send_json(200, {
                "id": f"mock-{n}",
                "object": "chat.completion",
                "model": body.get("model"),
                "choices": [{"index": 0, "message": {"role": "assistant", "content": content}, "finish_reason": "stop"}],
            })

    return Handler


def start(port: int = 0, **kwargs) -> tuple[ThreadingHTTPServer, MockState]:
    """Запустить в фоновом потоке. port=0 — любой свободный."""
    state = MockState(kwargs.pop("items", None) or load(), **kwargs)
    server = ThreadingHTTPServer(("127.0.0.1", port), make_handler(state))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server, state


def main() -> None:
    p = argparse.ArgumentParser(description="Подставной OpenAI-совместимый API и словарь")
    p.add_argument("--port", type=int, default=8089)
    p.add_argument("--api-key", help="требовать этот ключ")
    p.add_argument("--reject-json-mode", action="store_true", help="отвечать 400 на response_format")
    p.add_argument("--fail-every", type=int, default=0, help="каждый N-й запрос к модели — 503")
    args = p.parse_args()
    server, _ = start(args.port, api_key=args.api_key, reject_json_mode=args.reject_json_mode,
                      fail_every=args.fail_every)
    host, port = server.server_address
    print(f"модель:  http://{host}:{port}/v1\nсловарь: http://{host}:{port}{DICT_PREFIX}\nCtrl+C — остановить")
    try:
        threading.Event().wait()
    except KeyboardInterrupt:
        server.shutdown()


if __name__ == "__main__":
    main()
