import unittest

from easylang_eval.dataset import load
from easylang_eval.judge import NONE_OPTION, build_judge_prompt, mask_phrase, options_for, parse_choice
from easylang_eval.metrics import Row, mcnemar_exact, paired, variant_stats, wilson


class OptionsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.items = load()

    def test_options_contain_all_meanings_and_none_last(self):
        for it in self.items:
            o = options_for(it)
            self.assertEqual(set(o.texts[:-1]), {it.correct, *it.distractors})
            self.assertEqual(o.texts[o.correct], it.correct)
            self.assertEqual(o.texts[o.none], NONE_OPTION)

    def test_order_is_stable_and_not_always_first(self):
        self.assertEqual(options_for(self.items[0]), options_for(self.items[0]))
        positions = {options_for(it).correct for it in self.items}
        self.assertGreater(len(positions), 1)   # правильный смысл не всегда под номером 1

    def test_prompt_hides_phrase_context_and_numbers_from_one(self):
        it = self.items[0]
        p = build_judge_prompt(it, "Смысл: обиженный. Salty, he is SALTY.", options_for(it))
        self.assertNotIn("Phrase:", p)
        self.assertNotIn("salty", p.lower())
        self.assertIn("Смысл: обиженный. […], he is […].", p)
        self.assertNotIn(it.line, p)
        self.assertNotIn(it.title, p)
        self.assertIn("1. ", p)

    def test_kind(self):
        o = options_for(self.items[0])
        wrong = next(i for i in range(len(o.texts)) if i not in (o.correct, o.none))
        self.assertEqual([o.kind(o.correct), o.kind(o.none), o.kind(wrong), o.kind(None)],
                         ["correct", "none", "distractor", "unparsed"])


class MaskTests(unittest.TestCase):
    def test_masks_phrase_and_its_words_but_not_parts_of_other_words(self):
        self.assertEqual(mask_phrase("cap: A close-fitting hat. No cap!", "no cap"), "[…]: A close-fitting hat. […]!")
        self.assertEqual(mask_phrase("captain of the team", "no cap"), "captain of the team")
        self.assertEqual(mask_phrase("Don't spill the tea", "spill the tea"), "Don't […]")
        self.assertEqual(mask_phrase("caught in 4K", "caught in 4K"), "[…]")


class ParseChoiceTests(unittest.TestCase):
    def test_variants(self):
        self.assertEqual(parse_choice('{"choice": 2}', 4), 1)
        self.assertEqual(parse_choice('```json\n{"choice": "3"}\n```', 4), 2)
        self.assertEqual(parse_choice("I pick 4.", 4), 3)
        self.assertIsNone(parse_choice('{"choice": 9}', 4))
        self.assertIsNone(parse_choice("no idea", 4))


class MetricsTests(unittest.TestCase):
    def test_wilson(self):
        lo, hi = wilson(50, 100)
        self.assertAlmostEqual(lo, 0.404, places=3)
        self.assertAlmostEqual(hi, 0.596, places=3)
        self.assertEqual(wilson(0, 0), (0.0, 0.0))
        self.assertEqual(wilson(10, 10)[1], 1.0)

    def test_mcnemar(self):
        self.assertEqual(mcnemar_exact(0, 0), 1.0)
        self.assertAlmostEqual(mcnemar_exact(10, 0), 2 / 1024)
        self.assertAlmostEqual(mcnemar_exact(5, 5), 1.0)

    def test_stats_exclude_errors_and_pair_only_common(self):
        rows = [
            Row("a", "slang", "x", True, True, "correct", True),
            Row("b", "slang", "x", True, True, "distractor", True),
            Row("c", "slang", "x", False, False, "error", False),
            Row("a", "slang", "y", True, True, "distractor", True),
            Row("b", "slang", "y", True, True, "distractor", True),
            Row("c", "slang", "y", True, True, "correct", True),
        ]
        s = variant_stats(rows, "x")
        self.assertEqual((s.total, s.scored, s.errors, s.correct, s.accuracy), (3, 2, 1, 1, 0.5))
        p = paired(rows, "x", "y")
        self.assertEqual((p.both, p.only_a, p.only_b, p.neither), (0, 1, 0, 1))


if __name__ == "__main__":
    unittest.main()
