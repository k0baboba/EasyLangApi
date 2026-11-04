import json
import tempfile
import unittest
from collections import Counter
from pathlib import Path

from easylang_eval.dataset import CATEGORIES, DatasetError, contains_phrase, load, tokenize


class TokenizeTests(unittest.TestCase):
    def test_same_rules_as_phrase_normalizer(self):
        self.assertEqual(tokenize("Don’t  push it!"), ["don't", "push", "it"])
        self.assertEqual(tokenize("caught in 4K"), ["caught", "in", "4k"])

    def test_contains_phrase_needs_whole_words(self):
        self.assertTrue(contains_phrase("Okay, close the door and spill the tea.", "spill the tea"))
        self.assertFalse(contains_phrase("He stans the band.", "stan"))
        self.assertFalse(contains_phrase("anything", ""))


class BundledDatasetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.items = load()

    def test_has_150_items(self):
        self.assertEqual(len(self.items), 150)

    def test_categories(self):
        counts = Counter(i.category for i in self.items)
        self.assertEqual(set(counts), set(CATEGORIES))
        self.assertEqual(counts["slang"] + counts["polysemy"], 125)

    def test_ids_unique(self):
        self.assertEqual(len({i.id for i in self.items}), len(self.items))

    def test_controls_reuse_slang_words(self):
        # Контрольные фразы — те же слова, что и в сленге, но в буквальном смысле.
        slang_words = {tokenize(i.phrase)[0] for i in self.items if i.category == "slang"}
        controls = [i for i in self.items if i.category == "control"]
        reused = [i for i in controls if tokenize(i.phrase)[0] in slang_words or i.phrase == "spilled the tea"]
        self.assertGreaterEqual(len(reused), 20)


class ValidationTests(unittest.TestCase):
    GOOD = {
        "id": "x1", "category": "slang", "phrase": "salty", "title": "T", "speaker": "S",
        "before": ["a", "b"], "line": "He is salty.", "after": ["c", "d"],
        "correct": "обиженный", "distractors": ["солёный"],
    }

    def write(self, *rows) -> Path:
        d = tempfile.mkdtemp()
        p = Path(d) / "x.jsonl"
        p.write_text("\n".join(r if isinstance(r, str) else json.dumps(r) for r in rows), encoding="utf-8")
        return p

    def test_good_row_loads(self):
        self.assertEqual(load(self.write(self.GOOD))[0].phrase, "salty")

    def test_rejects_bad_rows(self):
        bad = [
            {**self.GOOD, "category": "other"},
            {**self.GOOD, "before": ["a"]},
            {**self.GOOD, "line": "He is sweet."},
            {**self.GOOD, "distractors": []},
            {**self.GOOD, "distractors": ["обиженный"]},
            {k: v for k, v in self.GOOD.items() if k != "correct"},
            "{not json",
        ]
        for row in bad:
            with self.subTest(row=row), self.assertRaises(DatasetError):
                load(self.write(row))

    def test_rejects_duplicate_ids(self):
        with self.assertRaises(DatasetError):
            load(self.write(self.GOOD, self.GOOD))


if __name__ == "__main__":
    unittest.main()
