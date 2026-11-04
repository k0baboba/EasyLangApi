namespace EasyLangApi.Episodes;

/// <summary>
/// Источник реплик: файл субтитров, скриншот и т.п. Всё, что вернул источник,
/// сохраняется одинаково (EpisodeImporter) и разбирается одним и тем же
/// GET /lines/{id}/explain — с теми же соседними репликами в контексте.
/// </summary>
public interface ITextSource
{
    Task<IReadOnlyList<SourceLine>> ReadLinesAsync(Stream content, CancellationToken cancellationToken);
}
