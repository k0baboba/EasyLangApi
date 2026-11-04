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
/// Параметры модели полураспада памяти: p(вспомнит через t дней) = 2^(-t / h),
/// log2(h) = Base + CorrectWeight·√(верных) + WrongWeight·√(ошибок) + d,
/// где d — поправка сложности конкретного элемента, подбирается по его истории.
/// </summary>
public sealed class AdaptiveOptions
{
    /// <summary>Какую долю вспоминаний держим: следующий показ — когда p(вспомнит) упадёт до этого значения.</summary>
    public double TargetRetention { get; set; } = 0.85;

    public double BaseLog2HalfLife { get; set; } = 2.0;
    public double CorrectWeight { get; set; } = 2.0;
    public double WrongWeight { get; set; } = -1.0;

    /// <summary>Насколько сильно держим поправку сложности около нуля, пока ответов мало (σ нормального априорного).</summary>
    public double DifficultyPriorSigma { get; set; } = 1.0;

    public double MinIntervalDays { get; set; } = 10.0 / (24 * 60);
    public double MaxIntervalDays { get; set; } = 365;
}
