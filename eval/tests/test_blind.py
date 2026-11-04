import csv
import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from easylang_eval import __main__ as cli
from easylang_eval import blind, mock_server
from easylang_eval.dataset import load
from easylang_eval.explainers import LlmConfig
from easylang_eval.runner import RunConfig, run

ITEMS = load()


class BlindTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        server, _ = mock_server.start()
        host, port = server.server_address
        cls.tmp = Path(tempfile.mkdtemp())
        llm = LlmConfig(f"http://{host}:{port}/v1", "", "m")
        cfg = RunConfig(out_dir=cls.tmp / "run", llm=llm, judge=llm, retries=0,
                        dictionary_url=f"http://{host}:{port}/api/v2/entries/en/")
        run(ITEMS[:30], cfg, log=lambda s: None)
        server.shutdown()
        server.server_close()

    def export(self, name: str, n: int = 60, seed: int = 2026) -> Path:
        blind.export(ITEMS, self.tmp / "run", self.tmp / name, n=n, seed=seed)
        return self.tmp / name

    def rows(self, d: Path) -> list[dict]:
        with open(d / "blind.csv", encoding="utf-8-sig", newline="") as f:
            return list(csv.DictReader(f))

    def test_export_hides_variant_and_verdict(self):
        d = self.export("a")
        rows = self.rows(d)
        self.assertEqual(len(rows), 60)
        self.assertEqual(set(rows[0]), {"row", "phrase", "explanation", "options", "human_choice", "comment"})
        text = (d / "blind.csv").read_text(encoding="utf-8-sig")
        for word in ("llm_context", "llm_plain", "dictionary", "correct", "distractor"):
            self.assertNotIn(word, text)
        key = json.loads((d / "blind_key.json").read_text(encoding="utf-8"))
        self.assertEqual(len({(r["id"], r["variant"]) for r in key["rows"]}), 60)   # без повторов
        self.assertTrue((d / "INSTRUCTIONS.md").exists())

    def test_export_is_reproducible_and_shuffled(self):
        a = json.loads((self.export("r1") / "blind_key.json").read_text(encoding="utf-8"))["rows"]
        b = json.loads((self.export("r2") / "blind_key.json").read_text(encoding="utf-8"))["rows"]
        self.assertEqual(a, b)
        self.assertGreater(len({r["variant"] for r in a[:10]}), 1)   # варианты перемешаны

    def test_export_refuses_too_small_pool(self):
        with self.assertRaises(ValueError):
            self.export("small", n=1000)

    def fill(self, d: Path, choose) -> None:
        key = {r["row"]: r for r in json.loads((d / "blind_key.json").read_text(encoding="utf-8"))["rows"]}
        rows = self.rows(d)
        for r in rows:
            r["human_choice"] = choose(key[int(r["row"])])
        with open(d / "blind.csv", "w", encoding="utf-8-sig", newline="") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0]))
            w.writeheader()
            w.writerows(rows)

    def test_full_agreement(self):
        d = self.export("agree")
        self.fill(d, lambda k: str(k["judge_choice"]))
        a = blind.score(d)
        self.assertEqual((a.n, a.exact, a.binary, a.skipped), (60, 60, 60, 0))
        self.assertAlmostEqual(a.kappa, 1.0)

    def test_disagreement_and_skips(self):
        d = self.export("dis")
        # Человек всегда отвечает «ни один», кроме двух пропусков.
        self.fill(d, lambda k: "" if k["row"] <= 2 else str(k["none_choice"]))
        a = blind.score(d)
        self.assertEqual((a.n, a.skipped), (58, 2))
        self.assertEqual(a.confusion[(True, True)] + a.confusion[(True, False)], 0)
        self.assertLess(a.binary, a.n)

    def test_kappa(self):
        self.assertIsNone(blind.cohen_kappa([]))
        self.assertAlmostEqual(blind.cohen_kappa([(True, True), (False, False)]), 1.0)
        self.assertAlmostEqual(blind.cohen_kappa([(True, False), (False, True)]), -1.0)
        self.assertIsNone(blind.cohen_kappa([(True, True), (True, True)]))

    def test_cli(self):
        out = self.tmp / "cli"
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(cli.main(["blind", "export", "--run", str(self.tmp / "run"), "--out", str(out)]), 0)
            self.assertEqual(cli.main(["blind", "score", "--dir", str(out)]), 0)
        self.assertIn("Оценено строк: 0, пропущено: 60", (out / "agreement.md").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
