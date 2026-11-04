"""Прогон целиком против подставного сервера.

Сервер отвечает по жёстким правилам (см. mock_server.py), поэтому ожидаемые числа
известны заранее. Тест проверяет связку компонентов, а не качество моделей.
"""

import csv
import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from easylang_eval import __main__ as cli
from easylang_eval import mock_server
from easylang_eval.dataset import load, tokenize
from easylang_eval.explainers import LlmConfig
from easylang_eval.runner import ConfigMismatch, RunConfig, read_jsonl, run

ITEMS = load()
N = len(ITEMS)
N_CONTROL = sum(1 for i in ITEMS if i.category == "control")
N_CONTROL_ONE_WORD = sum(1 for i in ITEMS if i.category == "control" and len(tokenize(i.phrase)) == 1)


def read(path: Path) -> list[dict]:
    return [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]


class MockRunTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())

    def start(self, **kw):
        server, state = mock_server.start(**kw)
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        host, port = server.server_address
        return f"http://{host}:{port}", state

    def config(self, base: str, **kw) -> RunConfig:
        llm = LlmConfig(f"{base}/v1", kw.pop("api_key", "test-key"), "mock-model", json_mode=kw.pop("json_mode", True))
        return RunConfig(out_dir=self.tmp / "run", llm=llm, judge=llm,
                         dictionary_url=f"{base}/api/v2/entries/en/", backoff=0.0, **kw)

    def kinds(self, variant: str, category: str | None = None) -> dict[str, int]:
        cat = {i.id: i.category for i in ITEMS}
        out: dict[str, int] = {}
        for r in read_jsonl(self.tmp / "run" / "judgments.jsonl").values():
            if r["variant"] == variant and r["ok"] and category in (None, cat[r["id"]]):
                out[r["kind"]] = out.get(r["kind"], 0) + 1
        return out

    def test_full_run_gives_expected_numbers(self):
        base, state = self.start(api_key="test-key", fail_every=13)
        report = run(ITEMS, self.config(base, workers=4), log=lambda s: None)

        self.assertEqual(self.kinds("llm_context"), {"correct": N})
        # Без контекста подставная модель даёт буквальное значение: верно только в контрольных.
        self.assertEqual(self.kinds("llm_plain", "control"), {"correct": N_CONTROL})
        for c in ("slang", "polysemy"):
            self.assertNotIn("correct", self.kinds("llm_plain", c))
            self.assertNotIn("correct", self.kinds("dictionary", c))
        # Словарь не знает выражений из нескольких слов.
        self.assertEqual(self.kinds("dictionary", "control").get("correct"), N_CONTROL_ONE_WORD)
        self.assertEqual(sum(self.kinds("dictionary").values()), N)

        text = report.read_text(encoding="utf-8")
        self.assertIn(f"| LLM с контекстом | {N} | 0 | {N} | 100.0 %", text)
        self.assertIn("Судья — та же модель", text)
        self.assertNotIn("## Ошибки", text)   # 503 от сервера ушли в повторы

        rows = list(csv.DictReader(io.StringIO((self.tmp / "run" / "results.csv").read_text(encoding="utf-8"))))
        self.assertEqual(len(rows), 3 * N)
        self.assertNotIn("test-key", (self.tmp / "run" / "run.json").read_text(encoding="utf-8"))

    def test_requests_carry_app_prompt_and_settings(self):
        base, state = self.start(api_key="test-key")
        run(ITEMS[:1], self.config(base, variants=("llm_plain", "llm_context")), log=lambda s: None)
        explain = [r for r in state.requests if r["method"] == "POST"
                   and r["body"]["messages"][0]["content"].startswith("You help")]
        self.assertEqual(len(explain), 2)
        for r in explain:
            self.assertEqual(r["authorization"], "Bearer test-key")
            self.assertEqual(r["body"]["model"], "mock-model")
            self.assertEqual(r["body"]["temperature"], 0.2)
            self.assertEqual(r["body"]["response_format"], {"type": "json_object"})
        users = sorted(r["body"]["messages"][1]["content"] for r in explain)
        with_ctx = next(u for u in users if "Line with the phrase" in u)
        self.assertIn(f"Work: {ITEMS[0].title}", with_ctx)
        self.assertIn(f"- {ITEMS[0].before[0]}", with_ctx)
        self.assertIn(f"- {ITEMS[0].after[1]}", with_ctx)
        self.assertTrue(any("No context is available" in u for u in users))

    def test_wrong_key_is_reported_not_scored(self):
        base, _ = self.start(api_key="right")
        report = run(ITEMS[:3], self.config(base, api_key="wrong", variants=("llm_context",)), log=lambda s: None)
        text = report.read_text(encoding="utf-8")
        self.assertIn("| LLM с контекстом | 0 | 3 | 0 | —", text)
        self.assertIn("ключ отклонён (401)", text)

    def test_json_mode_rejection_suggests_flag_and_flag_works(self):
        base, _ = self.start(reject_json_mode=True)
        cfg = self.config(base, variants=("llm_context",))
        answers = run(ITEMS[:2], cfg, log=lambda s: None).parent / "answers.jsonl"
        self.assertIn("--no-json-mode", read(answers)[0]["error"])

        cfg = self.config(base, variants=("llm_context",), json_mode=False)
        run(ITEMS[:2], cfg, fresh=True, log=lambda s: None)
        self.assertEqual(self.kinds("llm_context"), {"correct": 2})

    def test_resume_skips_done_and_retries_failed(self):
        base, state = self.start()
        cfg = self.config(base, variants=("llm_context",))
        run(ITEMS[:5], cfg, log=lambda s: None)
        before = len(state.requests)
        run(ITEMS[:5], cfg, log=lambda s: None)
        self.assertEqual(len(state.requests), before)   # ничего не запрашивали повторно

        # Отметим один ответ как сбой — при повторном запуске запросится только он.
        # Судья не нужен: текст ответа тот же, прежний вердикт к нему подходит.
        path = self.tmp / "run" / "answers.jsonl"
        with open(path, "a", encoding="utf-8") as f:
            f.write(json.dumps({"id": ITEMS[0].id, "variant": "llm_context", "ok": False, "error": "x"}) + "\n")
        run(ITEMS[:5], cfg, log=lambda s: None)
        self.assertEqual(len(state.requests), before + 1)

        # А если сломался вердикт — запросится только вердикт.
        jpath = self.tmp / "run" / "judgments.jsonl"
        with open(jpath, "a", encoding="utf-8") as f:
            f.write(json.dumps({"id": ITEMS[1].id, "variant": "llm_context", "ok": False, "error": "x",
                                "answer_text": ""}) + "\n")
        run(ITEMS[:5], cfg, log=lambda s: None)
        self.assertEqual(len(state.requests), before + 2)
        self.assertEqual(self.kinds("llm_context"), {"correct": 5})

    def test_changed_settings_need_fresh_or_new_folder(self):
        base, _ = self.start()
        run(ITEMS[:1], self.config(base, variants=("llm_context",)), log=lambda s: None)
        other = self.config(base, variants=("llm_context",))
        other.llm = LlmConfig(other.llm.base_url, "k", "other-model")
        with self.assertRaises(ConfigMismatch):
            run(ITEMS[:1], other, log=lambda s: None)

    def test_service_down_is_an_error_not_a_wrong_answer(self):
        cfg = self.config("http://127.0.0.1:9", variants=("llm_context",), retries=0)
        cfg.judge = cfg.llm
        text = run(ITEMS[:2], cfg, log=lambda s: None).read_text(encoding="utf-8")
        self.assertIn("| LLM с контекстом | 0 | 2 |", text)

    def test_cli_with_ollama_style_url_and_no_key(self):
        base, state = self.start()
        out = self.tmp / "cli"
        argv = ["run", "--base-url", f"{base}/v1/", "--model", "qwen2.5:7b", "--limit", "4",
                "--dictionary-url", f"{base}/api/v2/entries/en", "--out", str(out), "--retries", "0"]
        with redirect_stdout(io.StringIO()) as stdout, redirect_stderr(io.StringIO()):
            self.assertEqual(cli.main(argv), 0)
        self.assertEqual(Path(stdout.getvalue().strip()), out / "results.md")
        self.assertTrue(all(r.get("authorization") is None for r in state.requests if r["method"] == "POST"))
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(cli.main(["report", "--out", str(out)]), 0)

    def test_cli_requires_model(self):
        with redirect_stderr(io.StringIO()) as err:
            self.assertEqual(cli.main(["run", "--base-url", "http://x/v1"]), 2)
        self.assertIn("--model", err.getvalue())


if __name__ == "__main__":
    unittest.main()
