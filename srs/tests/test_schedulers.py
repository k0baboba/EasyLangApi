import math
import unittest

from srs_sim.events import HistoryFeatures, ReviewEvent
from srs_sim.schedulers import AdaptiveScheduler, FixedIntervalScheduler


def ev(correct: bool, elapsed: int = 1, day: int = 0, item: int = 0) -> ReviewEvent:
    return ReviewEvent(item_id=item, day=day, elapsed_days=elapsed, was_correct=correct)


class HistoryFeaturesTests(unittest.TestCase):
    def test_empty_history(self):
        f = HistoryFeatures.from_history([])
        self.assertEqual((f.n_correct, f.n_wrong, f.last_correct, f.streak), (0, 0, None, 0))

    def test_counts_and_streak(self):
        f = HistoryFeatures.from_history([ev(True), ev(False), ev(True), ev(True)])
        self.assertEqual((f.n_correct, f.n_wrong, f.last_correct, f.streak), (3, 1, True, 2))

    def test_streak_is_zero_after_mistake(self):
        f = HistoryFeatures.from_history([ev(True), ev(True), ev(False)])
        self.assertEqual(f.streak, 0)
        self.assertFalse(f.last_correct)


class FixedIntervalSchedulerTests(unittest.TestCase):
    def test_new_item_gets_first_step(self):
        self.assertEqual(FixedIntervalScheduler().next_interval([]), 1)

    def test_correct_answers_climb_the_ladder(self):
        s = FixedIntervalScheduler(steps=(1, 3, 7))
        self.assertEqual(s.next_interval([ev(True)]), 3)
        self.assertEqual(s.next_interval([ev(True), ev(True)]), 7)

    def test_ladder_saturates_at_last_step(self):
        s = FixedIntervalScheduler(steps=(1, 3, 7))
        self.assertEqual(s.next_interval([ev(True)] * 10), 7)

    def test_mistake_resets_to_first_step(self):
        s = FixedIntervalScheduler(steps=(1, 3, 7))
        self.assertEqual(s.next_interval([ev(True), ev(True), ev(False)]), 1)
        self.assertEqual(s.next_interval([ev(True), ev(True), ev(False), ev(True)]), 3)

    def test_observe_does_not_change_schedule(self):
        s = FixedIntervalScheduler()
        before = s.next_interval([ev(True)])
        for _ in range(100):
            s.observe([], ev(False, elapsed=50))
        self.assertEqual(s.next_interval([ev(True)]), before)

    def test_rejects_bad_steps(self):
        with self.assertRaises(ValueError):
            FixedIntervalScheduler(steps=())
        with self.assertRaises(ValueError):
            FixedIntervalScheduler(steps=(1, 0))


class AdaptiveSchedulerTests(unittest.TestCase):
    def test_initial_half_life_is_one_day(self):
        s = AdaptiveScheduler()
        self.assertAlmostEqual(s.half_life([]), 1.0)
        self.assertAlmostEqual(s.recall_probability([], 1), 0.5)
        self.assertAlmostEqual(s.forgetting_probability([], 1), 0.5)

    def test_recall_probability_decreases_with_time(self):
        s = AdaptiveScheduler(initial_weights=[3.0, 0.0, 0.0, 0.0])
        ps = [s.recall_probability([], t) for t in (0, 1, 5, 20, 100)]
        self.assertEqual(ps[0], 1.0)
        self.assertTrue(all(a > b for a, b in zip(ps, ps[1:])))

    def test_interval_hits_target_recall(self):
        # h = 2^5 = 32 дня; p(t) = 0.9 при t = 32 * log2(1/0.9) ≈ 4.86 -> 5 дней
        s = AdaptiveScheduler(target_recall=0.9, initial_weights=[5.0, 0.0, 0.0, 0.0])
        expected = round(32 * math.log2(1 / 0.9))
        self.assertEqual(s.next_interval([]), expected)

    def test_lower_target_means_longer_intervals(self):
        w = [5.0, 0.0, 0.0, 0.0]
        strict = AdaptiveScheduler(target_recall=0.95, initial_weights=w).next_interval([])
        lax = AdaptiveScheduler(target_recall=0.7, initial_weights=w).next_interval([])
        self.assertLess(strict, lax)

    def test_interval_is_clamped(self):
        self.assertEqual(AdaptiveScheduler(initial_weights=[-50, 0, 0, 0]).next_interval([]), 1)
        self.assertEqual(AdaptiveScheduler(initial_weights=[50, 0, 0, 0], max_interval=200).next_interval([]), 200)

    def test_correct_answer_after_long_gap_increases_half_life(self):
        s = AdaptiveScheduler()
        before = s.half_life([])
        s.observe([], ev(True, elapsed=3))   # модель ждала p = 1/8, а ученик вспомнил
        self.assertGreater(s.half_life([]), before)

    def test_mistake_soon_after_learning_decreases_half_life(self):
        s = AdaptiveScheduler(initial_weights=[3.0, 0.0, 0.0, 0.0])
        before = s.half_life([])
        s.observe([], ev(False, elapsed=1))
        self.assertLess(s.half_life([]), before)

    def test_learns_from_consistent_data(self):
        # Ученик всегда вспоминает через 5 дней — модель должна поднять p(5 дней) выше 0.8.
        s = AdaptiveScheduler()
        for _ in range(500):
            s.observe([], ev(True, elapsed=5))
        self.assertGreater(s.recall_probability([], 5), 0.8)

    def test_brier_score_tracks_predictions(self):
        s = AdaptiveScheduler()
        self.assertIsNone(s.brier_score())
        s.observe([], ev(True, elapsed=1))    # прогноз 0.5, ответ 1 -> (0.5)^2
        self.assertAlmostEqual(s.brier_score(), 0.25)

    def test_validation(self):
        with self.assertRaises(ValueError):
            AdaptiveScheduler(target_recall=1.0)
        with self.assertRaises(ValueError):
            AdaptiveScheduler(initial_weights=[1.0])


if __name__ == "__main__":
    unittest.main()
