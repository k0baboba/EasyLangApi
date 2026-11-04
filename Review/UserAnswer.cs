namespace EasyLangApi.Review;

/// <summary>
/// Один ответ пользователя в виде, нужном обучаемой модели: строка ReviewEvent
/// с ответом плюс момент первого знакомства с элементом (от него считается первый интервал).
/// </summary>
public sealed record UserAnswer(
    int ReviewEventId,
    int KnowledgeItemId,
    DateTime ItemFirstSeenAt,
    DateTime ShownAt,
    DateTime AnsweredAt,
    bool WasCorrect);
