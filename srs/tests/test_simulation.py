import unittest

from srs_sim.learner import LearnerParams, SyntheticLearner
from srs_sim.schedulers import AdaptiveScheduler, FixedIntervalScheduler
from srs_sim.simulation import SimConfig, run_many, simulate

SMALL = SimConfig(days=40, intro_days=20, new_items_per_day=5)


class LearnerTests(unittest.TestCase):
    def test_recall_is_one_right_after_introduction_and_decays(self):
        l = SyntheticLearner(seed=1)
        l.introduce(0, day=0)
        self.assertEqual(l.recall_probability(0, 0), 1.0)
        self.assertGreater(l.recall_probability(0, 1), l.recall_probability(0, 10))

    def test_stability_is_ninety_percent_point(self):
        l = SyntheticLearner(seed=1)
        l.introduce(0, day=0)
        l.items[0].stability = 10.0
        self.assertAlmostEqual(l.recall_probability(0, 10), 0.9)
        self.assertAlmostEqual(l.recall_probability(0, 20), 0.81)

    def test_difficulty_in_range(self):
        l = SyntheticLearner(seed=7)
        for i in range(200):
            l.introduce(i, 0)
        self.assertTrue(all(1.0 <= m.difficulty <= 10.0 for m in l.items.values()))

    def test_success_increases_stability_and_failure_decreases(self):
        params = LearnerParams(slip=0.0)
        # Ответ сразу после знакомства (R = 1) всегда верный.
        l = SyntheticLearner(seed=1, params=params)
        l.introduce(0, 0)
        s0 = l.items[0].stability
        self.assertTrue(l.review(0, 0))
        self.assertGreaterEqual(l.items[0].stability, s0)

        # Через очень долгий срок R ≈ 0 — ответ неверный, стабильность падает.
        l.introduce(1, 0)
        s1 = l.items[1].stability
        self.assertFalse(l.review(1, 10_000))
        self.assertLess(l.items[1].stability, s1)

    def test_late_successful_review_strengthens_more(self):
        # Эффект интервалов: при успехе прирост больше, если R было ниже.
        def gain_after(days: int) -> float:
            l = SyntheticLearner(seed=3, params=LearnerParams(slip=0.0))
            l.introduce(0, 0)
            s0 = l.items[0].stability
            l._rng_answers.random = lambda: 0.0   # гарантированный успех
            l.review(0, days)
            return l.items[0].stability / s0

        self.assertGreater(gain_after(5), gain_after(1))

    def test_same_seed_gives_same_items(self):
        a, b = SyntheticLearner(seed=5), SyntheticLearner(seed=5)
        for i in range(50):
            a.introduce(i, 0)
            b.introduce(i, 0)
        self.assertEqual([m.difficulty for m in a.items.values()], [m.difficulty for m in b.items.values()])


class SimulationTests(unittest.TestCase):
    def test_reproducible_with_same_seed(self):
        r1 = simulate(AdaptiveScheduler(), SMALL, seed=42)
        r2 = simulate(AdaptiveScheduler(), SMALL, seed=42)
        self.assertEqual(r1, r2)

    def test_different_seeds_differ(self):
        r1 = simulate(FixedIntervalScheduler(), SMALL, seed=1)
        r2 = simulate(FixedIntervalScheduler(), SMALL, seed=2)
        self.assertNotEqual(r1.retention_by_day, r2.retention_by_day)

    def test_basic_accounting(self):
        r = simulate(FixedIntervalScheduler(), SMALL, seed=1)
        self.assertEqual(r.n_items, SMALL.intro_days * SMALL.new_items_per_day)
        self.assertEqual(len(r.retention_by_day), SMALL.days)
        self.assertEqual(sum(r.reviews_by_day), r.total_reviews)
        self.assertLessEqual(r.correct_reviews, r.total_reviews)
        self.assertTrue(0.0 <= r.final_retention <= 1.0)
        self.assertLessEqual(r.known_items, r.n_items)
        self.assertEqual(r.retention_by_day[0], 0.0)  # в первый день слов ещё нет
        self.assertIsNone(r.brier)

    def test_daily_budget_is_respected(self):
        cfg = SimConfig(days=40, intro_days=20, new_items_per_day=10, daily_budget=7)
        r = simulate(AdaptiveScheduler(), cfg, seed=1)
        self.assertLessEqual(max(r.reviews_by_day), 7)
        self.assertGreater(r.backlog_end, 0)

    def test_no_backlog_without_budget(self):
        r = simulate(FixedIntervalScheduler(), SMALL, seed=1)
        self.assertEqual(r.backlog_end, 0)

    def test_reviews_beat_no_reviews(self):
        never = FixedIntervalScheduler(steps=(10_000,))
        no_rev = simulate(never, SMALL, seed=1)
        with_rev = simulate(FixedIntervalScheduler(), SMALL, seed=1)
        self.assertEqual(no_rev.total_reviews, 0)
        self.assertGreater(with_rev.final_retention, no_rev.final_retention)

    def test_adaptive_accuracy_tracks_target(self):
        # Адаптивный планировщик должен держать долю верных ответов около целевой.
        cfg = SimConfig(days=120, intro_days=60, new_items_per_day=10)
        r = simulate(AdaptiveScheduler(target_recall=0.85), cfg, seed=1)
        self.assertAlmostEqual(r.accuracy, 0.85, delta=0.05)
        self.assertIsNotNone(r.brier)

    def test_run_many_uses_fresh_scheduler_per_seed(self):
        rs = run_many(AdaptiveScheduler, SMALL, [1, 1])
        self.assertEqual(rs[0], rs[1])


if __name__ == "__main__":
    unittest.main()
