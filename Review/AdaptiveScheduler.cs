using EasyLangApi.Data;
using Microsoft.Extensions.Options;

namespace EasyLangApi.Review;

/// <summary>
/// Адаптивный планировщик на модели полураспада памяти (по мотивам half-life regression, Settles &amp; Meeder, 2016).
///
/// 1. Память о фразе убывает экспоненциально: p(вспомнит через t) = 2^(-t/h), h — период полураспада в днях.
/// 2. h растёт с числом верных ответов и падает с ошибками (веса из настроек — общая модель).
/// 3. Для конкретного элемента по его ReviewEvent подбирается поправка сложности d:
///    та, при которой модель лучше всего предсказывает уже случившиеся ответы
///    (максимум правдоподобия с нормальным априорным распределением, поиск по сетке).
///    Если фразу вспоминали после долгих перерывов, d растёт и интервалы удлиняются.
/// 4. Следующий показ — момент, когда p(вспомнит) упадёт до TargetRetention.
///
/// В Ease сохраняется множитель периода полураспада 2^d: 1 — обычная фраза, больше — лёгкая.
/// </summary>
public sealed class AdaptiveScheduler(IOptions<ReviewOptions> options) : IReviewScheduler
{
    private const double GridMin = -3, GridMax = 3, GridStep = 0.05;

    private AdaptiveOptions Settings => options.Value.Adaptive;

    public ReviewSchedule Schedule(KnowledgeItem item, IReadOnlyList<ReviewEvent> history, DateTime now)
    {
        var observations = Observe(item, history);
        var difficulty = FitDifficulty(observations);
        var (correct, wrong) = Count(history);

        var halfLife = HalfLifeDays(correct, wrong, difficulty);
        var interval = Math.Clamp(-halfLife * Math.Log2(Settings.TargetRetention), Settings.MinIntervalDays, Settings.MaxIntervalDays);
        return new ReviewSchedule(interval, now.AddDays(interval), Math.Pow(2, difficulty));
    }

    public double? ForgettingProbability(KnowledgeItem item, IReadOnlyList<ReviewEvent> history, DateTime at)
    {
        var difficulty = FitDifficulty(Observe(item, history));
        var (correct, wrong) = Count(history);
        var last = LastAnswerTime(item, history);
        var elapsedDays = Math.Max(0, (at - last).TotalDays);
        return 1 - RecallProbability(elapsedDays, HalfLifeDays(correct, wrong, difficulty));
    }

    internal double HalfLifeDays(int correct, int wrong, double difficulty) =>
        Math.Pow(2, Settings.BaseLog2HalfLife
                    + Settings.CorrectWeight * Math.Sqrt(correct)
                    + Settings.WrongWeight * Math.Sqrt(wrong)
                    + difficulty);

    internal static double RecallProbability(double elapsedDays, double halfLifeDays) =>
        Math.Pow(2, -elapsedDays / halfLifeDays);

    /// <summary>Одно наблюдение: сколько дней прошло с прошлого ответа, что было известно до него и вспомнил ли.</summary>
    internal sealed record Observation(double ElapsedDays, int CorrectBefore, int WrongBefore, bool Recalled);

    internal static List<Observation> Observe(KnowledgeItem item, IReadOnlyList<ReviewEvent> history)
    {
        var result = new List<Observation>();
        var previous = item.FirstSeenAt;
        int correct = 0, wrong = 0;
        foreach (var answer in Answered(history))
        {
            var elapsed = Math.Max(0, (answer.ShownAt - previous).TotalDays);
            result.Add(new Observation(elapsed, correct, wrong, answer.WasCorrect!.Value));
            if (answer.WasCorrect!.Value) correct++; else wrong++;
            previous = answer.AnsweredAt ?? answer.ShownAt;
        }

        return result;
    }

    /// <summary>Поправка сложности с наибольшим апостериорным правдоподобием.</summary>
    internal double FitDifficulty(IReadOnlyList<Observation> observations)
    {
        if (observations.Count == 0)
            return 0;

        var bestDifficulty = 0.0;
        var bestScore = double.NegativeInfinity;
        for (var d = GridMin; d <= GridMax + 1e-9; d += GridStep)
        {
            var score = LogPosterior(observations, d);
            if (score > bestScore)
            {
                bestScore = score;
                bestDifficulty = d;
            }
        }

        return Math.Round(bestDifficulty, 2);
    }

    private double LogPosterior(IReadOnlyList<Observation> observations, double difficulty)
    {
        var sigma = Settings.DifficultyPriorSigma;
        var score = -(difficulty * difficulty) / (2 * sigma * sigma);
        foreach (var o in observations)
        {
            // Ограничиваем p, чтобы ошибка сразу после показа не дала log(0).
            var p = Math.Clamp(RecallProbability(o.ElapsedDays, HalfLifeDays(o.CorrectBefore, o.WrongBefore, difficulty)), 1e-3, 1 - 1e-3);
            score += o.Recalled ? Math.Log(p) : Math.Log(1 - p);
        }

        return score;
    }

    private static IEnumerable<ReviewEvent> Answered(IReadOnlyList<ReviewEvent> history) =>
        history.Where(e => e.WasCorrect is not null).OrderBy(e => e.ShownAt).ThenBy(e => e.Id);

    private static (int Correct, int Wrong) Count(IReadOnlyList<ReviewEvent> history)
    {
        var answered = Answered(history).ToList();
        var correct = answered.Count(e => e.WasCorrect == true);
        return (correct, answered.Count - correct);
    }

    private static DateTime LastAnswerTime(KnowledgeItem item, IReadOnlyList<ReviewEvent> history) =>
        Answered(history).Select(e => e.AnsweredAt ?? e.ShownAt).DefaultIfEmpty(item.FirstSeenAt).Max();
}
