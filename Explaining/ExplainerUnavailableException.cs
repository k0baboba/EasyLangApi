namespace EasyLangApi.Explaining;

/// <summary>
/// Внешний источник разбора недоступен или ответил ошибкой. Это не баг приложения,
/// а ожидаемая ситуация: клиент получает 503 и может повторить запрос позже.
/// </summary>
public sealed class ExplainerUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
