import csv
import gzip
import random
import tempfile
import unittest
from pathlib import Path

from srs_sim import duolingo as d
from srs_sim.events import ReviewEvent
from srs_sim.schedulers import AdaptiveScheduler

HEADER = ["p_recall", "timestamp", "delta", "user_id", "learning_language", "ui_language", "lexeme_id",
          "lexeme_string", "history_seen", "history_correct", "session_seen", "session_correct"]


def write_csv(rows: list[list], gz: bool = True) -> Path:
    path = Path(tempfile.mkdtemp()) / ("t.csv.gz" if gz else "t.csv")
    opener = gzip.open if gz else open
    with opener(path, "wt", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(HEADER)
        w.writerows(rows)
    return path


def sequence_rows(answers: list[tuple[int, bool]], user="u1", lexeme="lx", start=1_000_000) -> list[list]:
    """Одна пара, в каждой сессии один ответ. answers: (дней с прошлого показа, верно?)."""
    rows, seen, correct, ts = [], 0, 0, start
    for days, ok in answers:
        ts += days * 86400
        rows.append([1.0 if ok else 0.0, ts, days * 86400, user, "en", "ru", lexeme, "word",
                     seen, correct, 1, 1 if ok else 0])
        seen += 1
        correct += ok
    return rows


class MatchesSchedulerTests(unittest.TestCase):
    def test_matches_adaptive_scheduler(self):
        rng = random.Random(7)
        answers = [(rng.randint(1, 30), rng.random() < 0.7) for _ in range(200)]
        t = d.load(write_csv(sequence_rows(answers)))
        order = d.time_order(t)
        d.derive_last_wrong(t, order)

        ours = d.HlrModel("hlr_ours", d.features_ours, 4)
        ref = AdaptiveScheduler()
        history: list[ReviewEvent] = []
        day = 0
        for i, (days, ok) in zip(order, answers):
            day += days
            ev = ReviewEvent(item_id=0, day=day, elapsed_days=days, was_correct=ok)
            self.assertEqual(ours.predict(t, i), ref.recall_probability(history, days))
            ours.update(t, i)
            ref.observe(history, ev)
            history.append(ev)
            self.assertEqual(ours.weights, ref.weights)


class LoadTests(unittest.TestCase):
    def test_load_skips_bad_rows_and_counts(self):
        rows = sequence_rows([(1, True), (2, False)]) + sequence_rows([(3, True)], user="u2")
        rows.append(["x", 1, 1, "u3", "en", "ru", "l", "w", 0, 0, 1, 1])     # не число
        rows.append([1.0, 1, 1, "u3", "en", "ru", "l", "w", 0, 0, 0, 0])     # session_seen = 0
        t = d.load(write_csv(rows, gz=False))
        self.assertEqual((len(t), t.skipped, t.users, t.lexemes), (3, 2, 2, 1))
        self.assertAlmostEqual(t.delta_days[1], 2.0)

    def test_missing_columns(self):
        path = Path(tempfile.mkdtemp()) / "bad.csv"
        path.write_text("a,b\n1,2\n", encoding="utf-8")
        with self.assertRaises(ValueError):
            d.load(path)

    def test_last_wrong_uses_only_past_in_time_order(self):
        # Строки в файле не по времени: признак должен считаться по времени, а не по порядку в файле.
        rows = sequence_rows([(1, True), (1, False), (1, True)])
        rows = [rows[2], rows[0], rows[1]]
        t = d.load(write_csv(rows))
        order = d.time_order(t)
        self.assertEqual(order, [1, 2, 0])
        d.derive_last_wrong(t, order)
        self.assertEqual([t.has_previous[i] for i in order], [0, 1, 1])
        self.assertEqual([t.last_wrong[i] for i in order], [0, 0, 1])


class MetricTests(unittest.TestCase):
    def test_auc(self):
        self.assertEqual(d.weighted_auc([0.9, 0.1], [1, 0], [0, 1]), 1.0)
        self.assertEqual(d.weighted_auc([0.1, 0.9], [1, 0], [0, 1]), 0.0)
        self.assertEqual(d.weighted_auc([0.5, 0.5], [3, 1], [1, 3]), 0.5)
        self.assertIsNone(d.weighted_auc([0.5], [1], [0]))

    def test_run_end_to_end(self):
        rng = random.Random(1)
        rows = []
        for u in range(30):
            rows += sequence_rows([(rng.randint(1, 10), rng.random() < 0.8) for _ in range(20)],
                                  user=f"u{u}", lexeme=f"l{u % 5}", start=1_000_000 + u * 3600)
        res = d.run(write_csv(rows), log=lambda s: None)
        self.assertEqual(res["data"]["rows"], 600)
        self.assertEqual(res["data"]["train_rows"] + res["data"]["test_rows"], 600)
        self.assertEqual(set(res["models"]), {"constant", "hlr_bias", "logistic", "hlr_ours"})
        self.assertEqual(res["models"]["constant"]["auc"], 0.5)
        for m in res["models"].values():
            self.assertTrue(0 <= m["mae"] <= 1 and 0 <= m["ece"] <= 1)
            self.assertEqual(sum(c["rows"] for c in m["calibration"]), res["data"]["test_rows"])
        md = d.render_markdown(res)
        self.assertIn("CC BY-NC 4.0", md)
        self.assertIn("Наш планировщик", md)


if __name__ == "__main__":
    unittest.main()
