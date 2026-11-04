namespace EasyLangApi.Lookups;

/// <summary>Соседний запрос одновременно создал тот же элемент словаря, и восстановиться не удалось → 409.</summary>
public sealed class DuplicatePhraseException(string message, Exception? innerException = null)
    : Exception(message, innerException);
