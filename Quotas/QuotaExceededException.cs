namespace EasyLangApi.Quotas;

/// <summary>Пользователь исчерпал квоту → 429.</summary>
public sealed class QuotaExceededException(string message) : Exception(message);
