"""Живая проверка приложения: настоящие словарь, языковая модель и Tesseract.

Запускается в workflow live-check (ветка run/live), но работает против любого запущенного API:

    python3 tools/live_check.py --dictionary http://localhost:8081 --llm http://localhost:8080 --out out/live

--dictionary — экземпляр без Llm:ApiKey (DictionaryExplainer), --llm — с ключом (LlmExplainer).
На каждом сервере регистрируется новый пользователь, загружается sample.srt и разбираются фразы
из PHRASES в той реплике, где они встречаются впервые. Ответы записываются как есть, без повторов:
что получил бы пользователь с первой попытки. Только стандартная библиотека.
"""

from __future__ import annotations

import argparse
import json
import re
import secrets
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PHRASES = ["shift", "beat", "casing", "on the house", "no cap", "pulling my leg", "gonna", "spill the beans"]
TITLE = "Night Shift S01E01"


def request(base: str, method: str, path: str, token: str | None = None, body: bytes | None = None,
            content_type: str | None = None, timeout: float = 1200) -> tuple[int, object, float]:
    req = urllib.request.Request(base.rstrip("/") + path, data=body, method=method)
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    if content_type:
        req.add_header("Content-Type", content_type)
    started = time.monotonic()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            status, raw = resp.status, resp.read()
    except urllib.error.HTTPError as e:
        status, raw = e.code, e.read()
    elapsed = time.monotonic() - started
    try:
        data = json.loads(raw) if raw else None
    except json.JSONDecodeError:
        data = raw.decode("utf-8", "replace")
    return status, data, elapsed


def multipart(fields: dict[str, str], files: dict[str, tuple[str, bytes, str]]) -> tuple[bytes, str]:
    boundary = "----easylang" + secrets.token_hex(8)
    parts: list[bytes] = []
    for name, value in fields.items():
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode())
    for name, (filename, content, ctype) in files.items():
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"; filename="{filename}"\r\n'
                     f"Content-Type: {ctype}\r\n\r\n".encode() + content + b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode())
    return b"".join(parts), f"multipart/form-data; boundary={boundary}"


def login(base: str) -> str:
    creds = {"email": f"live-{secrets.token_hex(4)}@example.com", "password": secrets.token_urlsafe(16)}
    status, data, _ = request(base, "POST", "/auth/register", body=json.dumps(creds).encode(),
                              content_type="application/json", timeout=60)
    if status != 201:
        raise RuntimeError(f"{base}: регистрация вернула {status}: {data}")
    return data["accessToken"]


def upload_sample(base: str, token: str) -> list[dict]:
    body, ctype = multipart({"title": TITLE}, {"file": ("sample.srt", (ROOT / "sample.srt").read_bytes(), "application/x-subrip")})
    status, episode, _ = request(base, "POST", "/episodes", token, body, ctype, timeout=60)
    if status != 201:
        raise RuntimeError(f"{base}: загрузка sample.srt вернула {status}: {episode}")
    status, lines, _ = request(base, "GET", f"/episodes/{episode['id']}/lines", token, timeout=60)
    return lines


def words(text: str) -> list[str]:
    return re.findall(r"[a-z0-9']+", text.lower().replace("’", "'"))


def first_line_with(phrase: str, lines: list[dict]) -> dict:
    target = words(phrase)
    for line in lines:
        w = words(line["text"])
        if any(w[i:i + len(target)] == target for i in range(len(w) - len(target) + 1)):
            return line
    raise LookupError(phrase)


def explain(base: str, token: str, line: dict, phrase: str) -> dict:
    query = urllib.parse.urlencode({"phrase": phrase})
    status, data, elapsed = request(base, "GET", f"/lines/{line['id']}/explain?{query}", token)
    result = {"status": status, "seconds": round(elapsed, 2)}
    if status == 200:
        result["explanation"] = data["explanation"]
        result["context"] = data["context"]
    else:
        result["problem"] = data
    return result


def recognize(base: str, token: str) -> dict:
    body, ctype = multipart({"title": "Harbor Lights"}, {"image": ("sample.png", (ROOT / "sample.png").read_bytes(), "image/png")})
    status, data, elapsed = request(base, "POST", "/recognize", token, body, ctype, timeout=120)
    return {"status": status, "seconds": round(elapsed, 2), "response": data}


def summarize(result: dict) -> str:
    """Короткий текст ответа для таблицы."""
    if result["status"] != 200:
        problem = result.get("problem") or {}
        detail = problem.get("detail") if isinstance(problem, dict) else problem
        return f"**{result['status']}**: {detail}"
    e = result["explanation"]
    if e["source"] == "llm":
        parts = [e.get("translation"), e.get("summary"), e.get("contextNote")]
        return " — ".join(p for p in parts if p)
    if e.get("found") and e.get("summary"):
        pos = e["senses"][0].get("partOfSpeech") if e.get("senses") else None
        return f"({pos}) {e['summary']}" if pos else e["summary"]
    senses = "; ".join(f"{s['word']}: {s['definition']}" for s in e.get("senses", [])[:2])
    return f"целиком не найдено; по словам: {senses}" if senses else "не найдено"


def cell(text: str, limit: int = 300) -> str:
    text = " ".join(str(text).split()).replace("|", "\\|")
    return text if len(text) <= limit else text[: limit - 1] + "…"


def render_table(rows: list[dict]) -> str:
    out = ["| Фраза | Реплика | Словарь (DictionaryExplainer) | Языковая модель (LlmExplainer) | Время, с: словарь / LLM |",
           "|---|---|---|---|---:|"]
    for r in rows:
        out.append(f"| `{r['phrase']}` | {cell(r['line'], 120)} | {cell(summarize(r['dictionary']))} "
                   f"| {cell(summarize(r['llm']))} | {r['dictionary']['seconds']} / {r['llm']['seconds']} |")
    return "\n".join(out)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--dictionary", required=True, help="API без Llm:ApiKey")
    parser.add_argument("--llm", required=True, help="API с Llm:ApiKey")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--limit", type=int, default=len(PHRASES), help="сколько фраз разобрать (пробный прогон — 1)")
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    servers = {"dictionary": args.dictionary, "llm": args.llm}
    tokens = {k: login(v) for k, v in servers.items()}
    lines = {k: upload_sample(servers[k], tokens[k]) for k in servers}

    rows = []
    for phrase in PHRASES[: args.limit]:
        row = {"phrase": phrase}
        for kind in servers:
            line = first_line_with(phrase, lines[kind])
            row["line"] = line["text"]
            row[kind] = explain(servers[kind], tokens[kind], line, phrase)
            print(f"{phrase!r} [{kind}] {row[kind]['status']} за {row[kind]['seconds']} с: {cell(summarize(row[kind]), 160)}", flush=True)
        rows.append(row)

    ocr = recognize(servers["dictionary"], tokens["dictionary"])
    print(f"recognize sample.png: {ocr['status']} за {ocr['seconds']} с", flush=True)

    (args.out / "results.json").write_text(json.dumps({"phrases": rows, "recognize": ocr}, ensure_ascii=False, indent=1), encoding="utf-8")
    table = render_table(rows)
    ocr_text = ocr["response"].get("text") if isinstance(ocr["response"], dict) else ocr["response"]
    (args.out / "table.md").write_text(
        f"# Живая проверка: {TITLE} (sample.srt)\n\n{table}\n\n"
        f"Распознавание `sample.png` (Tesseract в контейнере): статус {ocr['status']}, {ocr['seconds']} с, текст:\n\n"
        f"```\n{ocr_text}\n```\n", encoding="utf-8")
    print(table)


if __name__ == "__main__":
    main()
