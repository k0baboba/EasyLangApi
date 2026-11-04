import tempfile
import unittest
from pathlib import Path

from srs_sim.experiment import Scenario, Stat, run_experiment
from srs_sim.report import AUTO_BEGIN, AUTO_END, render_auto, update_report
from srs_sim.schedulers import AdaptiveScheduler, FixedIntervalScheduler
from srs_sim.simulation import SimConfig

SCENARIOS = (
    Scenario("a", "A", SimConfig(days=40, intro_days=20, new_items_per_day=5)),
    Scenario("b", "B", SimConfig(days=40, intro_days=20, new_items_per_day=5, daily_budget=5)),
)
FACTORIES = [
    ("fixed", FixedIntervalScheduler),
    ("adaptive@0.75", lambda: AdaptiveScheduler(0.75)),
    ("adaptive@0.85", lambda: AdaptiveScheduler(0.85)),
]
SEEDS = [11, 12, 13]


def build() -> str:
    return render_auto(run_experiment(SCENARIOS, SEEDS, FACTORIES), SCENARIOS, SEEDS)


class StatTests(unittest.TestCase):
    def test_mean_and_sd(self):
        s = Stat.of([1.0, 2.0, 3.0])
        self.assertAlmostEqual(s.mean, 2.0)
        self.assertAlmostEqual(s.sd, 1.0)

    def test_single_value_has_zero_sd(self):
        self.assertEqual(Stat.of([5.0]).sd, 0.0)


class ReportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.auto = build()

    def test_report_is_reproducible(self):
        self.assertEqual(build(), self.auto)

    def test_contains_markers_tables_and_charts(self):
        self.assertTrue(self.auto.startswith(AUTO_BEGIN))
        self.assertTrue(self.auto.endswith(AUTO_END))
        self.assertEqual(self.auto.count("xychart-beta"), len(SCENARIOS))
        for name, _ in FACTORIES:
            self.assertEqual(self.auto.count(f"| `{name}` |"), len(SCENARIOS))

    def test_chart_has_one_line_per_chosen_scheduler(self):
        chart = self.auto.split("```mermaid")[1].split("```")[0]
        self.assertEqual(chart.count("    line ["), 3)

    def test_update_preserves_handwritten_text(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "REPORT.md"
            path.write_text(f"# Head\n\nintro\n\n{AUTO_BEGIN}\nold\n{AUTO_END}\n\n## Вывод\n\ntext\n", encoding="utf-8")
            update_report(path, self.auto)
            text = path.read_text(encoding="utf-8")
            self.assertIn("intro", text)
            self.assertIn("## Вывод\n\ntext", text)
            self.assertNotIn("\nold\n", text)
            self.assertEqual(text.count(AUTO_BEGIN), 1)

    def test_update_creates_missing_file(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "REPORT.md"
            update_report(path, self.auto)
            self.assertIn(AUTO_BEGIN, path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
