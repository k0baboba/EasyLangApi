namespace EasyLangApi.Quotas;

public sealed class QuotaOptions
{
    public const string SectionName = "Quotas";

    /// <summary>Сколько запросов к дорогим эндпоинтам (разбор, распознавание) в минуту на пользователя.</summary>
    public int ExpensiveRequestsPerMinute { get; set; } = 30;

    /// <summary>Сколько разборов фраз в сутки (UTC) на пользователя. Считаются сохранённые Lookup.</summary>
    public int LookupsPerDay { get; set; } = 500;
}
