"""Полный прогон эксперимента и сверка с закоммиченным REPORT.md.

Медленный (~30 с), поэтому запускается только с SRS_FULL=1.
"""

import os
import unittest
from pathlib import Path

from srs_sim.experiment import SCENARIOS, run_experiment, seeds
from srs_sim.report import AUTO_BEGIN, AUTO_END, render_auto

REPORT = Path(__file__).resolve().parent.parent / "REPORT.md"


@unittest.skipUnless(os.environ.get("SRS_FULL") == "1", "полный прогон: SRS_FULL=1")
class ReportMatchesFreshRun(unittest.TestCase):
    def test_committed_report_is_reproduced(self):
        text = REPORT.read_text(encoding="utf-8")
        committed = text[text.index(AUTO_BEGIN): text.index(AUTO_END) + len(AUTO_END)]
        seed_list = seeds()
        fresh = render_auto(run_experiment(SCENARIOS, seed_list), SCENARIOS, seed_list)
        self.assertEqual(fresh, committed)


if __name__ == "__main__":
    unittest.main()
