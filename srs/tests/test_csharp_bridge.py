"""C#-планировщик приложения принимает те же решения, что и Python-версия.

Нужен .NET SDK и сборка моста (srs/bridge), поэтому запускается только с SRS_CSHARP=1:

    SRS_CSHARP=1 python3 -m unittest tests.test_csharp_bridge
"""

import os
import unittest

from srs_sim.experiment import Scenario
from srs_sim.simulation import SimConfig


@unittest.skipUnless(os.environ.get("SRS_CSHARP") == "1", "нужен .NET: SRS_CSHARP=1")
class CSharpSchedulerMatchesPython(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from srs_sim.csharp_check import build_bridge
        cls.bridge = build_bridge()

    def check(self, scenario: Scenario, target: float, seed: int):
        from srs_sim.csharp_check import compare_run
        r = compare_run(scenario, target, seed, self.bridge)
        self.assertGreater(r.decisions, 100)
        self.assertTrue(r.same_decisions, f"первое различие в решении №{r.first_diff}")
        self.assertTrue(r.same_forgetting, f"max |Δp забыть| = {r.max_forgetting_diff}")
        self.assertEqual(r.python.total_reviews, r.csharp.total_reviews)
        self.assertEqual(r.python.known_items, r.csharp.known_items)

    def test_unlimited(self):
        self.check(Scenario("a", "A", SimConfig(days=60, intro_days=30, new_items_per_day=5)), 0.8, 7)

    def test_with_budget_and_forgetting_order(self):
        self.check(Scenario("c", "C", SimConfig(days=60, intro_days=30, new_items_per_day=5, daily_budget=20,
                                                queue_order="forgetting")), 0.85, 9)

    def test_with_daily_budget(self):
        self.check(Scenario("b", "B", SimConfig(days=60, intro_days=30, new_items_per_day=5, daily_budget=20)), 0.9, 8)


if __name__ == "__main__":
    unittest.main()
