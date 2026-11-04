import re
import unittest
from pathlib import Path

from easylang_eval.prompts import (EMPTY_CONTEXT, PhraseContext, build_system_prompt, build_user_prompt,
                                   parse_llm_answer)

CSHARP = Path(__file__).resolve().parents[2] / "Explaining" / "PromptBuilder.cs"


class UserPromptTests(unittest.TestCase):
    """Те же случаи, что в tests/EasyLangApi.Tests/Unit/PromptBuilderTests.cs."""

    def test_full_context_in_order(self):
        ctx = PhraseContext(
            title="Night Shift S01E01", speaker="LEO",
            before=("Rain on the window.", "You look tired."),
            current_line="LEO: I'm beat.",
            after=("Coffee's on the house.", "Thanks."),
        )
        self.assertEqual(build_user_prompt("beat", ctx), "\n".join([
            'Phrase: "beat"',
            "Work: Night Shift S01E01",
            "Speaker: LEO",
            "Previous lines:",
            "- Rain on the window.",
            "- You look tired.",
            "Line with the phrase: LEO: I'm beat.",
            "Next lines:",
            "- Coffee's on the house.",
            "- Thanks.",
        ]))

    def test_missing_parts_are_skipped(self):
        prompt = build_user_prompt("hit the road", PhraseContext(before=("", "  "), current_line="Hit the road."))
        for absent in ("Work:", "Speaker:", "Previous lines:", "Next lines:", "No context"):
            self.assertNotIn(absent, prompt)
        self.assertIn("Line with the phrase: Hit the road.", prompt)

    def test_without_context_asks_for_common_meaning(self):
        prompt = build_user_prompt("no cap", EMPTY_CONTEXT)
        self.assertTrue(prompt.startswith('Phrase: "no cap"'))
        self.assertIn("No context is available", prompt)
        self.assertNotIn("Line with the phrase", prompt)


class SystemPromptTests(unittest.TestCase):
    def test_language_and_json(self):
        p = build_system_prompt("Russian")
        self.assertIn("Russian-speaking learner", p)
        self.assertIn('"translation"', p)
        self.assertIn("JSON", p)

    @unittest.skipUnless(CSHARP.exists(), "PromptBuilder.cs не найден рядом")
    def test_matches_csharp_source(self):
        """Промпт должен совпадать с приложением, иначе оценка мерит не то."""
        src = CSHARP.read_text(encoding="utf-8")
        m = re.search(r'BuildSystemPrompt\(string explanationLanguage\) => \$\$"""\n(.*?)\n( *)""";', src, re.S)
        self.assertIsNotNone(m, "не нашёл raw-строку системного промпта в PromptBuilder.cs")
        indent = len(m.group(2))
        lines = [l[indent:] if l.strip() else "" for l in m.group(1).split("\n")]
        expected = "\n".join(lines).replace("{{explanationLanguage}}", "Russian")
        self.assertEqual(build_system_prompt("Russian"), expected)


class ParseTests(unittest.TestCase):
    def test_clean_json(self):
        a = parse_llm_answer('{"translation": "без вранья", "meaning": "честно", "contextNote": ""}')
        self.assertTrue(a.json_ok and a.found)
        self.assertEqual((a.translation, a.summary, a.context_note), ("без вранья", "честно", None))
        self.assertEqual(a.text_for_judge(), "Перевод: без вранья\nСмысл: честно")

    def test_json_in_markdown_and_case_insensitive_keys(self):
        a = parse_llm_answer('Sure!\n```json\n{"Meaning": "уходим"}\n```\nHope it helps.')
        self.assertTrue(a.json_ok)
        self.assertEqual(a.summary, "уходим")
        self.assertEqual(a.text_for_judge(), "Смысл: уходим")

    def test_translation_only(self):
        a = parse_llm_answer('{"translation": "ничья"}')
        self.assertEqual(a.text_for_judge(), "Перевод: ничья")

    def test_not_json_keeps_raw_text(self):
        a = parse_llm_answer("  It means 'to leave'.  ")
        self.assertFalse(a.json_ok)
        self.assertEqual(a.text_for_judge(), "It means 'to leave'.")

    def test_broken_json_and_empty(self):
        self.assertEqual(parse_llm_answer("{meaning: broken").summary, "{meaning: broken")
        self.assertFalse(parse_llm_answer("   ").found)
        self.assertFalse(parse_llm_answer('{"meaning": "  "}').json_ok)


if __name__ == "__main__":
    unittest.main()
