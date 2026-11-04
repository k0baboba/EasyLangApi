# Шаг 09. Нарушение уникальности — это 409, а не 500

Задача 5 «Гонка при создании элемента» · тема: исключения · ~8 минут · в guide.md это задача 4

## Условие

Два одновременных разбора одной новой фразы (`beat` и `Beat!` нормализуются
одинаково). Оба не находят `KnowledgeItem`, оба вставляют, и второй
`SaveChangesAsync` падает на уникальном индексе `(UserId, PhraseNormalized)`.
Сейчас клиент получает 500, как при баге.

На этом шаге научитесь **узнавать** именно эту ошибку: проигравший запрос
должен получить 409 Conflict в формате ProblemDetails. Другие ошибки базы
по-прежнему должны давать 500. Чинить гонку полностью будем на шаге 10.

Тест делает гонку воспроизводимой: `SaveChangesBarrier`
(`exercises/Exercises.Tests/Infrastructure/`) придерживает первые два
`SaveChanges`, пока до записи не дойдут оба запроса. Посмотрите, как он
устроен, — на собеседовании это хороший пример.

- Где: `Lookups/LookupService.cs` и `Infrastructure/KnownExceptionHandler.cs`.
- Тест: `exercises/Exercises.Tests/Step09/ParallelNewPhraseTests.cs`.

## Почитать

- [Рекомендации по исключениям](https://learn.microsoft.com/dotnet/standard/exceptions/best-practices-for-exceptions)
- [Фильтры исключений `when`](https://learn.microsoft.com/dotnet/csharp/language-reference/statements/exception-handling-statements#exception-filters)
- [Коды ошибок SQLite](https://www.sqlite.org/rescode.html), раздел SQLITE_CONSTRAINT

## Подсказка 1

Запустите тест и прочитайте стек в выводе: какой тип исключения, какое у него
`InnerException` и какой там код ошибки SQLite?

## Подсказка 2

Как в проекте уже превращают «ожидаемое» исключение в код ответа? Посмотрите,
что умеет `KnownExceptionHandler`, и подумайте, нужно ли ему знать про EF.

## Подсказка 3

Ловить нужно не «любую ошибку записи», а только нарушение уникальности —
фильтром `when` по `InnerException`. Поймали — бросьте своё, понятное
исключение и научите обработчик отвечать на него 409. Остальные
`DbUpdateException` пусть летят дальше.
