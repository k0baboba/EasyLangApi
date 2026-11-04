namespace EasyLangApi.Data;

// Модель данных из SPEC.md. Ключевая идея: фраза — единица показа (Line, Lookup),
// слово/выражение — единица учёта (KnowledgeItem).

/// <summary>Источник текста: файл субтитров (позже — и скриншот).</summary>
public class Episode
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public List<Line> Lines { get; set; } = [];
}

/// <summary>Одна реплика с таймингами. Index — порядковый номер внутри эпизода, с нуля.</summary>
public class Line
{
    public int Id { get; set; }
    public int EpisodeId { get; set; }
    public Episode Episode { get; set; } = null!;
    public int Index { get; set; }
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// Наблюдение: что именно посмотрели и в каком контексте.
/// KnowledgeItemId добавлен к схеме из SPEC.md, чтобы не искать элемент учёта по строке.
/// </summary>
public class Lookup
{
    public int Id { get; set; }
    public int LineId { get; set; }
    public Line Line { get; set; } = null!;
    public int KnowledgeItemId { get; set; }
    public KnowledgeItem KnowledgeItem { get; set; } = null!;
    public string Phrase { get; set; } = "";
    public string ExplanationJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Элемент учёта: что отслеживаем и когда показать снова.</summary>
public class KnowledgeItem
{
    public int Id { get; set; }
    public string PhraseNormalized { get; set; } = "";
    public DateTime FirstSeenAt { get; set; }
    public double IntervalDays { get; set; }
    public DateTime DueAt { get; set; }
    public double Ease { get; set; }

    public List<Lookup> Lookups { get; set; } = [];
    public List<ReviewEvent> ReviewEvents { get; set; } = [];
}

/// <summary>
/// Сырая история показов и ответов. Строки только добавляются и дописываются
/// (ответ на показ), но никогда не удаляются и не схлопываются.
/// </summary>
public class ReviewEvent
{
    public int Id { get; set; }
    public int KnowledgeItemId { get; set; }
    public KnowledgeItem KnowledgeItem { get; set; } = null!;
    public DateTime ShownAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public bool? WasCorrect { get; set; }
}
