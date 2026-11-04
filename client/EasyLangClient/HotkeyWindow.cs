namespace EasyLangClient;

/// <summary>
/// Невидимое окно, которое получает WM_HOTKEY. Хоткей глобальный: работает, даже когда
/// активна игра (если она не перехватывает клавиатуру монопольно).
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;

    public event EventHandler? Pressed;

    public HotkeyWindow(string hotkey)
    {
        CreateHandle(new CreateParams());
        var (modifiers, key) = Parse(hotkey);
        if (!Native.RegisterHotKey(Handle, HotkeyId, modifiers | Native.Modifiers.NoRepeat, (uint)key))
            throw new InvalidOperationException($"Не удалось зарегистрировать хоткей {hotkey}: он занят другой программой.");
    }

    /// <summary>"Ctrl+Shift+E" → (Control | Shift, Keys.E).</summary>
    public static (Native.Modifiers Modifiers, Keys Key) Parse(string hotkey)
    {
        var modifiers = Native.Modifiers.None;
        Keys? key = null;
        foreach (var part in hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.Modifiers.Control; break;
                case "shift": modifiers |= Native.Modifiers.Shift; break;
                case "alt": modifiers |= Native.Modifiers.Alt; break;
                case "win": modifiers |= Native.Modifiers.Win; break;
                default:
                    key = Enum.TryParse<Keys>(part, ignoreCase: true, out var parsed)
                        ? parsed
                        : throw new FormatException($"Непонятная клавиша '{part}' в хоткее '{hotkey}'.");
                    break;
            }
        }

        return (modifiers, key ?? throw new FormatException($"В хоткее '{hotkey}' нет основной клавиши."));
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WmHotkey && m.WParam == HotkeyId)
            Pressed?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Native.UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle();
    }
}
