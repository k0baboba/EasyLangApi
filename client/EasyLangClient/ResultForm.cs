using System.Text;
using System.Text.RegularExpressions;

namespace EasyLangClient;

/// <summary>
/// Окно с распознанным текстом. Клик по слову — выбрать его, Shift+клик — добавить к фразе
/// (для выражений из нескольких слов). "Разобрать" отправляет фразу на сервер вместе с id реплики:
/// контекст (соседние реплики, название) сервер соберёт сам.
/// </summary>
internal sealed partial class ResultForm : Form
{
    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex WordRegex();

    private readonly ApiClient _api;
    private readonly TextBox _phrase = new() { Width = 300, Name = "phrase" };
    private readonly Button _explain = new() { Text = "Разобрать", AutoSize = true, Name = "explain" };
    private readonly TextBox _output = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10),
        Name = "output",
    };
    private int? _selectedLineId;

    public ResultForm(ApiClient api, RecognitionResult result, string? title)
    {
        _api = api;
        Name = "EasyLangResult";
        Text = string.IsNullOrWhiteSpace(title) ? "EasyLang" : $"EasyLang — {title}";
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Location = Cursor.Position;
        Size = new Size(520, 480);
        Font = new Font("Segoe UI", 10);

        var lines = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            MaximumSize = new Size(500, 220), AutoScroll = true,
        };
        if (result.Lines.Count == 0)
            lines.Controls.Add(new Label { Text = "Текст не найден. Попробуйте выделить область крупнее.", AutoSize = true });
        foreach (var line in result.Lines)
            lines.Controls.Add(CreateLineRow(line));

        var input = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        input.Controls.Add(_phrase);
        input.Controls.Add(_explain);

        Controls.Add(_output);
        Controls.Add(input);
        Controls.Add(lines);

        AcceptButton = _explain;
        _explain.Click += async (_, _) => await ExplainAsync();
        if (result.Lines.Count > 0)
            _selectedLineId = result.Lines[0].Id;
    }

    private FlowLayoutPanel CreateLineRow(RecognizedLine line)
    {
        var row = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 0, 0, 6) };
        foreach (Match word in WordRegex().Matches(line.Text))
        {
            var link = new LinkLabel
            {
                Text = word.Value, AutoSize = true, Margin = new Padding(0, 0, 2, 0), Name = $"word-{line.Index}-{word.Index}",
            };
            link.LinkClicked += (_, _) =>
            {
                var append = ModifierKeys.HasFlag(Keys.Shift) && _selectedLineId == line.Id && _phrase.Text.Length > 0;
                _phrase.Text = append ? $"{_phrase.Text} {word.Value}" : word.Value;
                _selectedLineId = line.Id;
            };
            row.Controls.Add(link);
        }

        return row;
    }

    private async Task ExplainAsync()
    {
        if (_selectedLineId is not { } lineId || string.IsNullOrWhiteSpace(_phrase.Text))
            return;

        _explain.Enabled = false;
        _output.Text = "Разбираю…";
        try
        {
            var result = await _api.ExplainAsync(lineId, _phrase.Text.Trim());
            _output.Text = Format(result);
        }
        catch (ApiException ex)
        {
            _output.Text = ex.Message;
        }
        finally
        {
            _explain.Enabled = true;
        }
    }

    private static string Format(LookupResult result)
    {
        var e = result.Explanation;
        var text = new StringBuilder();
        text.AppendLine(e.Phonetic is null ? e.Phrase : $"{e.Phrase}  {e.Phonetic}");
        if (e.Translation is not null) text.AppendLine($"Перевод: {e.Translation}");
        if (e.Summary is not null) text.AppendLine(e.Summary);
        if (e.ContextNote is not null) text.AppendLine().AppendLine(e.ContextNote);
        if (!e.Found && e.Senses.Count > 0) text.AppendLine("Целиком не найдено, значения отдельных слов:");
        foreach (var sense in e.Senses)
        {
            text.AppendLine().Append("• ");
            if (sense.Word != e.Phrase) text.Append($"{sense.Word}: ");
            if (sense.PartOfSpeech is not null) text.Append($"({sense.PartOfSpeech}) ");
            text.AppendLine(sense.Definition);
            if (sense.Example is not null) text.AppendLine($"   «{sense.Example}»");
        }

        text.AppendLine().Append(result.IsNewItem ? "Добавлено в словарь." : "Уже в словаре.");
        return text.ToString().Replace("\n", Environment.NewLine);
    }
}
