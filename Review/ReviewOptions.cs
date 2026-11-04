namespace EasyLangApi.Review;

public enum SchedulerKind
{
    Fixed,
    Adaptive,
}

public sealed class ReviewOptions
{
    public const string SectionName = "Review";

    public SchedulerKind Scheduler { get; set; } = SchedulerKind.Fixed;

    /// <summary>
    /// Ступеньки фиксированного планировщика в днях. null — 1, 3, 7, 14, 30, 60, 120.
    /// (Списку нельзя задать значения по умолчанию: биндер конфигурации дописал бы к ним элементы из файла.)
    /// </summary>
    public double[]? FixedIntervals { get; set; }

    /// <summary>Элемент с интервалом не меньше этого считается выученным при отборе фраз.</summary>
    public double KnownAfterDays { get; set; } = 21;

    public AdaptiveOptions Adaptive { get; set; } = new();
}

/// <summary>
/// Параметры адаптивного планировщика (обучаемая half-life regression из srs/).
/// Значения по умолчанию — как в srs/srs_sim/schedulers.py, кроме цели: 0.8 вместо 0.9,
/// потому что srs/REPORT.md рекомендует 0.8 при отсутствии дневного лимита повторений.
/// </summary>
public sealed class AdaptiveOptions
{
    /// <summary>Какую вероятность вспомнить держим: следующий показ — когда предсказанная p упадёт до неё.</summary>
    public double TargetRetention { get; set; } = 0.8;

    /// <summary>Шаг градиентного спуска.</summary>
    public double LearningRate { get; set; } = 0.05;

    /// <summary>Сила L2-регуляризации весов.</summary>
    public double L2 { get; set; } = 1e-4;

    /// <summary>Начальные веса [bias, √верных, √ошибок, последний — ошибка]. null — нули (h = 1 дню).</summary>
    public double[]? InitialWeights { get; set; }

    /// <summary>Округлять интервал до целых дней, как в симуляции srs/.</summary>
    public bool RoundToWholeDays { get; set; } = true;

    public double MinIntervalDays { get; set; } = 1;
    public double MaxIntervalDays { get; set; } = 365;
}
