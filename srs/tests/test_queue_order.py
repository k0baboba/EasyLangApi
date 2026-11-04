import unittest

from srs_sim.events import ReviewEvent
from srs_sim.schedulers import AdaptiveScheduler, FixedIntervalScheduler
from srs_sim.simulation import SimConfig, order_queue, simulate


class FakeRiskScheduler:
    """Планировщик с заданными вероятностями забыть по id элемента."""

    def __init__(self, risk: dict[int, float]) -> None:
        self.risk = risk
        self.calls = []

    def forgetting_many(self, entries):
        self.calls.append([i for i, _, _ in entries])
        return [self.risk[i] for i, _, _ in entries]


DUE = [(5, 1), (3, 2), (3, 3), (4, 4)]   # (срок, id)
HIST = {i: [] for i in range(1, 5)}
LAST = {i: 0 for i in range(1, 5)}


class OrderQueueTests(unittest.TestCase):
    def test_due_order_is_by_date_then_id(self):
        self.assertEqual(order_queue(FakeRiskScheduler({}), "due", 6, list(DUE), HIST, LAST),
                         [(3, 2), (3, 3), (4, 4), (5, 1)])

    def test_forgetting_order_puts_highest_risk_first_and_breaks_ties_by_date(self):
        s = FakeRiskScheduler({1: 0.9, 2: 0.1, 3: 0.5, 4: 0.5})
        self.assertEqual(order_queue(s, "forgetting", 6, list(DUE), HIST, LAST),
                         [(5, 1), (3, 3), (4, 4), (3, 2)])

    def test_scheduler_without_probabilities_keeps_date_order(self):
        self.assertEqual(order_queue(FixedIntervalScheduler(), "forgetting", 6, list(DUE), HIST, LAST),
                         sorted(DUE))

    def test_unknown_order_is_rejected(self):
        with self.assertRaises(ValueError):
            order_queue(FakeRiskScheduler({}), "random", 6, list(DUE), HIST, LAST)

    def test_adaptive_forgetting_many_matches_single_calls(self):
        s = AdaptiveScheduler(initial_weights=[2.0, 0.5, -0.3, 0.1])
        h = [ReviewEvent(1, 3, 3, True), ReviewEvent(1, 5, 2, False)]
        self.assertEqual(s.forgetting_many([(1, h, 4), (2, [], 1)]),
                         [s.forgetting_probability(h, 4), s.forgetting_probability([], 1)])


class SimulationWithForgettingOrderTests(unittest.TestCase):
    CONFIG = SimConfig(days=60, intro_days=30, new_items_per_day=5, daily_budget=10, queue_order="forgetting")

    def test_is_reproducible(self):
        a = simulate(AdaptiveScheduler(0.85), self.CONFIG, seed=3)
        b = simulate(AdaptiveScheduler(0.85), self.CONFIG, seed=3)
        self.assertEqual((a.total_reviews, a.known_items, a.final_retention), (b.total_reviews, b.known_items, b.final_retention))

    def test_budget_is_respected(self):
        r = simulate(AdaptiveScheduler(0.85), self.CONFIG, seed=3)
        self.assertLessEqual(max(r.reviews_by_day), 10)

    def test_order_changes_the_outcome_under_budget(self):
        by_due = simulate(AdaptiveScheduler(0.85), SimConfig(**{**self.CONFIG.__dict__, "queue_order": "due"}), seed=3)
        by_risk = simulate(AdaptiveScheduler(0.85), self.CONFIG, seed=3)
        self.assertNotEqual(by_due.retention_by_day, by_risk.retention_by_day)


if __name__ == "__main__":
    unittest.main()
