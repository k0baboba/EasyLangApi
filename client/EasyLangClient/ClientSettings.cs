using System.Text.Json;

namespace EasyLangClient;

/// <summary>Настройки из settings.json рядом с exe.</summary>
public sealed class ClientSettings
{
    public string ServerUrl { get; set; } = "http://localhost:5057";
    public string Email { get; set; } = "";

    /// <summary>Например "Ctrl+Shift+E" или "Alt+F9".</summary>
    public string Hotkey { get; set; } = "Ctrl+Shift+E";

    /// <summary>Отправлять заголовок активного окна как название игры — сервер добавит его в контекст.</summary>
    public bool UseWindowTitleAsTitle { get; set; } = true;

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static ClientSettings Load()
    {
        if (!File.Exists(FilePath))
            return new ClientSettings();
        return JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(FilePath), Json) ?? new ClientSettings();
    }

    /// <summary>Сохраняет настройки. Папка программы может быть закрыта на запись — тогда просто не запоминаем.</summary>
    public bool TrySave()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
