namespace EasyLangApi.Episodes;

/// <summary>Реплика, извлечённая из источника, до сохранения в базу.</summary>
public sealed record SourceLine(int StartMs, int EndMs, string Text);
