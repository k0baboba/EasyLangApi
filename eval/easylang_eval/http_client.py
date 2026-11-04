"""Минимальный HTTP-клиент на urllib: JSON, таймаут, повторы при временных сбоях."""

from __future__ import annotations

import json
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass

LOCAL_HOSTS = {"localhost", "127.0.0.1", "::1"}
RETRY_STATUSES = {408, 429, 500, 502, 503, 504}


class ServiceError(RuntimeError):
    """Сервис недоступен или ответил ошибкой. status=None — сбой соединения или таймаут."""

    def __init__(self, message: str, status: int | None = None, body: str = "") -> None:
        super().__init__(message)
        self.status = status
        self.body = body


@dataclass(frozen=True)
class Response:
    status: int
    body: str

    def json(self):
        return json.loads(self.body)


@dataclass
class HttpClient:
    timeout: float = 60.0
    retries: int = 3
    backoff: float = 2.0          # пауза перед повтором: backoff, 2*backoff, 4*backoff...
    sleep = staticmethod(time.sleep)

    def request(self, method: str, url: str, payload: dict | None = None,
                headers: dict[str, str] | None = None) -> Response:
        data = None if payload is None else json.dumps(payload, ensure_ascii=False).encode("utf-8")
        all_headers = {"Accept": "application/json", "User-Agent": "easylang-eval/1.0"}
        if data is not None:
            all_headers["Content-Type"] = "application/json"
        all_headers.update(headers or {})

        attempt = 0
        while True:
            try:
                return self._send(method, url, data, all_headers)
            except ServiceError as e:
                retryable = e.status is None or e.status in RETRY_STATUSES
                if not retryable or attempt >= self.retries:
                    raise
                self.sleep(self.backoff * (2 ** attempt))
                attempt += 1

    def _send(self, method: str, url: str, data: bytes | None, headers: dict[str, str]) -> Response:
        req = urllib.request.Request(url, data=data, headers=headers, method=method)
        host = urllib.parse.urlsplit(url).hostname or ""
        # Локальный сервер (Ollama, заглушка) — напрямую, мимо системного прокси.
        handlers = [urllib.request.ProxyHandler({})] if host in LOCAL_HOSTS else []
        opener = urllib.request.build_opener(*handlers)
        try:
            with opener.open(req, timeout=self.timeout) as resp:
                return Response(resp.status, resp.read().decode("utf-8", errors="replace"))
        except urllib.error.HTTPError as e:
            body = e.read().decode("utf-8", errors="replace")
            if e.code == 404:
                return Response(404, body)
            raise ServiceError(f"{url} ответил кодом {e.code}: {body[:300]}", e.code, body) from None
        except (urllib.error.URLError, TimeoutError, ConnectionError, OSError) as e:
            reason = getattr(e, "reason", e)
            raise ServiceError(f"{url}: не удалось соединиться ({reason})") from None


def join_url(base: str, path: str) -> str:
    """Как HttpClient.BaseAddress в .NET: base 'http://x/v1' + 'chat/completions' -> 'http://x/v1/chat/completions'."""
    return base.rstrip("/") + "/" + path.lstrip("/")
