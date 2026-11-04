namespace EasyLangApi.Review;

/// <summary>Сводка истории ответов по одному элементу — всё, что модель знает об элементе.</summary>
public readonly record struct ItemStats(int Correct, int Wrong, bool? LastCorrect)
{
    public ItemStats After(bool wasCorrect) =>
        new(Correct + (wasCorrect ? 1 : 0), Wrong + (wasCorrect ? 0 : 1), wasCorrect);

    public static ItemStats From(IEnumerable<bool> answers) =>
        answers.Aggregate(default(ItemStats), (stats, correct) => stats.After(correct));
}

/// <summary>
/// Обучаемая модель забывания — перенос AdaptiveScheduler из srs/srs_sim/schedulers.py
/// (half-life regression, Settles &amp; Meeder 2016). Формулы и порядок операций совпадают
/// с Python-версией, чтобы на одних и тех же данных получались те же числа.
///
///   p(вспомнит через t дней) = 2^(-t / h),   h = 2^(θ · x),
///   x = [1, √верных, √ошибок, последний ответ — ошибка].
///
/// Веса θ общие для всех элементов пользователя и подбираются онлайн-градиентным спуском
/// по (p − y)² после каждого ответа y ∈ {0, 1}, с L2-регуляризацией и клиппингом градиента.
/// </summary>
public sealed class HalfLifeRegression
{
    public static readonly IReadOnlyList<string> FeatureNames = ["bias", "sqrt_correct", "sqrt_wrong", "last_wrong"];

    public const double MinLog2HalfLife = -4.0; // h >= 1/16 дня
    public const double MaxLog2HalfLife = 12.0; // h <= 4096 дней

    private static readonly double Ln2 = Math.Log(2.0);

    private readonly double[] _weights;
    private readonly double _learningRate;
    private readonly double _l2;

    public HalfLifeRegression(double learningRate, double l2, IReadOnlyList<double>? initialWeights = null)
    {
        if (initialWeights is not null && initialWeights.Count != FeatureNames.Count)
            throw new ArgumentException($"Нужно {FeatureNames.Count} начальных весов.", nameof(initialWeights));
        // Старт с нулей: h = 1 день для нового элемента, остальные признаки нейтральны.
        _weights = initialWeights?.ToArray() ?? new double[FeatureNames.Count];
        _learningRate = learningRate;
        _l2 = l2;
    }

    public IReadOnlyList<double> Weights => _weights;

    /// <summary>Сколько ответов модель уже видела.</summary>
    public int ObservationCount { get; private set; }

    public static double[] Features(ItemStats stats) =>
    [
        1.0,
        Math.Sqrt(stats.Correct),
        Math.Sqrt(stats.Wrong),
        stats.LastCorrect == false ? 1.0 : 0.0,
    ];

    public double HalfLifeDays(ItemStats stats)
    {
        var x = Features(stats);
        var log2HalfLife = 0.0;
        for (var i = 0; i < x.Length; i++)
            log2HalfLife += _weights[i] * x[i];
        // Ограничиваем, чтобы ранний шум в весах не дал h = 0 или бесконечность.
        log2HalfLife = Math.Max(MinLog2HalfLife, Math.Min(log2HalfLife, MaxLog2HalfLife));
        return Math.Pow(2.0, log2HalfLife);
    }

    public double RecallProbability(ItemStats stats, double elapsedDays) =>
        Math.Pow(2.0, -elapsedDays / HalfLifeDays(stats));

    /// <summary>Один шаг обучения: что было известно об элементе до ответа, сколько дней прошло, вспомнил ли.</summary>
    public void Observe(ItemStats before, double elapsedDays, bool wasCorrect)
    {
        var x = Features(before);
        var h = HalfLifeDays(before);
        var t = elapsedDays;
        var p = Math.Pow(2.0, -t / h);
        var y = wasCorrect ? 1.0 : 0.0;

        // dp/dθ = p · ln2 · (t / h) · ln2 · x; производная (p − y)² даёт множитель 2(p − y).
        var common = 2.0 * (p - y) * p * Ln2 * Ln2 * t / h;
        for (var i = 0; i < x.Length; i++)
        {
            var grad = common * x[i] + _l2 * _weights[i];
            // Клиппинг градиента: защита от скачков на редких длинных интервалах.
            grad = Math.Max(-1.0, Math.Min(1.0, grad));
            _weights[i] -= _learningRate * grad;
        }

        ObservationCount++;
    }
}
