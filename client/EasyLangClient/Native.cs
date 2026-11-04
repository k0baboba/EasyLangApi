using System.Runtime.InteropServices;
using System.Text;

namespace EasyLangClient;

/// <summary>Функции Win32, которых нет в WinForms.</summary>
internal static partial class Native
{
    public const int WmHotkey = 0x0312;

    [Flags]
    public enum Modifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000,
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, Modifiers modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    public static string GetForegroundWindowTitle()
    {
        var text = new StringBuilder(256);
        return GetWindowText(GetForegroundWindow(), text, text.Capacity) > 0 ? text.ToString() : "";
    }
}
