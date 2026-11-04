namespace EasyLangApi.Review;

/// <summary>
/// Простейший пересчёт интервала: лесенка фиксированных шагов.
/// Помнил — следующая ступенька, не помнил — снова с первой.
/// </summary>
public static class IntervalLadder
{
    public static readonly IReadOnlyList<double> DefaultSteps = [1, 3, 7, 14, 30, 60, 120];

    public static double Next(double currentIntervalDays, bool wasCorrect, IReadOnlyList<double>? steps = null)
    {
        steps ??= DefaultSteps;
        if (!wasCorrect)
            return steps[0];

        foreach (var step in steps)
            if (step > currentIntervalDays)
                return step;

        // За последней ступенькой — удваиваем.
        return currentIntervalDays * 2;
    }
}
