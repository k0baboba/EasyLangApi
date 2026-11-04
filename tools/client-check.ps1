# Проверка десктопного клиента на Windows (workflow client-check, раннер windows-latest).
#
# Сервер уже запущен (http://localhost:5057), пользователь зарегистрирован, клиент собран.
# Скрипт по шагам: показывает окно-«сцену» с sample.png, запускает клиент, входит через UI Automation,
# нажимает хоткей через SendInput, выделяет область мышью, кликает слова в окне результата
# и нажимает «Разобрать». После каждого шага — скриншот и строка в report.md: удалось или нет.
# Шаги независимы настолько, насколько возможно: провал одного записывается, следующий пробуется.

param(
    [Parameter(Mandatory)] [string] $ClientExe,
    [Parameter(Mandatory)] [string] $Password,
    [Parameter(Mandatory)] [string] $OutDir,
    [string] $ScenePng = (Resolve-Path (Join-Path $PSScriptRoot '..' 'sample.png')).Path,
    [string] $SceneTitle = 'Harbor Lights'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes

Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class Desk
{
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public INPUTUNION u; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);

    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1, KEYUP = 2;
    const uint MOVE = 0x0001, LEFTDOWN = 0x0002, LEFTUP = 0x0004, ABSOLUTE = 0x8000, VIRTUALDESK = 0x4000;

    static uint Send(params INPUT[] inputs) { return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))); }

    static INPUT Key(ushort vk, bool up)
    {
        var i = new INPUT { type = INPUT_KEYBOARD };
        i.u.ki.wVk = vk; i.u.ki.dwFlags = up ? KEYUP : 0;
        return i;
    }

    static INPUT Mouse(int x, int y, uint flags)
    {
        var v = SystemInformation.VirtualScreen;
        var i = new INPUT { type = INPUT_MOUSE };
        i.u.mi.dx = (int)Math.Round((x - v.Left) * 65535.0 / Math.Max(1, v.Width - 1));
        i.u.mi.dy = (int)Math.Round((y - v.Top) * 65535.0 / Math.Max(1, v.Height - 1));
        i.u.mi.dwFlags = flags | MOVE | ABSOLUTE | VIRTUALDESK;
        return i;
    }

    /// Нажать сочетание клавиш: модификаторы вниз, клавиша, всё вверх в обратном порядке. Возвращает число принятых событий.
    public static uint Chord(params ushort[] keys)
    {
        var list = new System.Collections.Generic.List<INPUT>();
        foreach (var k in keys) list.Add(Key(k, false));
        for (int i = keys.Length - 1; i >= 0; i--) list.Add(Key(keys[i], true));
        return Send(list.ToArray());
    }

    public static void KeyDown(ushort vk) { Send(Key(vk, false)); }
    public static void KeyUp(ushort vk) { Send(Key(vk, true)); }

    public static void MoveTo(int x, int y) { Send(Mouse(x, y, 0)); }

    /// Протянуть мышь с зажатой левой кнопкой — так пользователь выделяет область.
    public static void Drag(int x1, int y1, int x2, int y2, int steps)
    {
        Send(Mouse(x1, y1, 0));
        System.Threading.Thread.Sleep(100);
        Send(Mouse(x1, y1, LEFTDOWN));
        for (int s = 1; s <= steps; s++)
        {
            Send(Mouse(x1 + (x2 - x1) * s / steps, y1 + (y2 - y1) * s / steps, 0));
            System.Threading.Thread.Sleep(15);
        }
        Send(Mouse(x2, y2, LEFTUP));
    }

    public static void Click(int x, int y)
    {
        Send(Mouse(x, y, 0));
        System.Threading.Thread.Sleep(50);
        Send(Mouse(x, y, LEFTDOWN), Mouse(x, y, LEFTUP));
    }

    /// SetForegroundWindow срабатывает, только если наш процесс «последним получил ввод»: нажимаем Alt.
    public static bool Focus(IntPtr hwnd)
    {
        Chord(0x12);
        return SetForegroundWindow(hwnd);
    }

    public static string ForegroundTitle()
    {
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(GetForegroundWindow(), sb, sb.Capacity);
        return sb.ToString();
    }

    public static string Screenshot(string path)
    {
        var v = SystemInformation.VirtualScreen;
        using (var bmp = new Bitmap(v.Width, v.Height, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(v.Left, v.Top, 0, 0, v.Size, CopyPixelOperation.SourceCopy);
            bmp.Save(path, ImageFormat.Png);
        }
        return v.Width + "x" + v.Height;
    }
}
'@

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$report = [System.Collections.Generic.List[string]]::new()
$shot = 0
$failed = 0

function Note([string] $text) { Write-Host $text; $report.Add($text) }

function Shot([string] $name) {
    $script:shot++
    $file = '{0:00}-{1}.png' -f $script:shot, $name
    try { $size = [Desk]::Screenshot((Join-Path $OutDir $file)); Note "  скриншот ``$file`` ($size)" }
    catch { Note "  скриншот не снят: $($_.Exception.Message)" }
}

function Step([string] $title, [scriptblock] $body) {
    Note ""
    Note "### $title"
    try { & $body; Note "**удалось**" }
    catch { $script:failed++; Note "**не удалось**: $($_.Exception.Message)" }
}

$UIA = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]

function Find-ById([System.Windows.Automation.AutomationElement] $parent, [string] $id, [int] $seconds = 20,
                   [System.Windows.Automation.TreeScope] $scope = [System.Windows.Automation.TreeScope]::Descendants) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($UIA::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $el = $parent.FindFirst($scope, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    throw "не найден элемент '$id' за $seconds с"
}

function Find-Window([string] $id, [int] $seconds = 30) { Find-ById $UIA::RootElement $id $seconds $Tree::Children }

function Center([System.Windows.Automation.AutomationElement] $el) {
    $r = $el.Current.BoundingRectangle
    return @([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
}

function Invoke-Button([System.Windows.Automation.AutomationElement] $el) {
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Value-Of([System.Windows.Automation.AutomationElement] $el) {
    $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

Note "# Проверка клиента на Windows"
Note ""
Note "Раннер: $([Environment]::OSVersion.VersionString), пользователь $env:USERNAME, интерактивная сессия: $([Environment]::UserInteractive)."
Note "Экраны: $(([System.Windows.Forms.Screen]::AllScreens | ForEach-Object { "$($_.Bounds.Width)x$($_.Bounds.Height)" }) -join ', ')."

$scene = $null
$client = $null
$sceneRectFile = Join-Path $OutDir 'scene-rect.json'

Step 'Окно-«сцена» с текстом (как окно игры)' {
    # Отдельный процесс PowerShell показывает sample.png в окне с заголовком игры и пишет,
    # где на экране оказалась картинка.
    $sceneScript = @"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
`$f = New-Object System.Windows.Forms.Form
`$f.Text = '$SceneTitle'
`$f.StartPosition = 'Manual'
`$f.Location = New-Object System.Drawing.Point(0, 0)
`$f.ClientSize = New-Object System.Drawing.Size(1000, 260)
`$f.BackColor = [System.Drawing.Color]::White
`$pb = New-Object System.Windows.Forms.PictureBox
`$pb.Image = [System.Drawing.Image]::FromFile('$ScenePng')
`$pb.SizeMode = 'Zoom'
`$pb.Location = New-Object System.Drawing.Point(10, 10)
`$pb.Size = New-Object System.Drawing.Size(980, 240)
`$f.Controls.Add(`$pb)
`$f.Add_Shown({ `$r = `$pb.RectangleToScreen(`$pb.ClientRectangle); @{ x = `$r.X; y = `$r.Y; w = `$r.Width; h = `$r.Height } | ConvertTo-Json | Set-Content '$sceneRectFile' })
[System.Windows.Forms.Application]::Run(`$f)
"@
    $script:scene = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', $sceneScript -PassThru
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path $sceneRectFile) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    if (-not (Test-Path $sceneRectFile)) { throw 'окно-сцена не показалось за 30 с' }
    Note "  картинка на экране: $(Get-Content $sceneRectFile -Raw)"
    Shot 'scene'
}

Step 'Запуск клиента и вход через окно входа' {
    $script:client = Start-Process $ClientExe -PassThru -WorkingDirectory (Split-Path $ClientExe)
    $login = Find-Window 'EasyLangLogin' 40
    Note "  окно входа: «$($login.Current.Name)»"
    Shot 'login'
    $password = Find-ById $login 'password'
    $password.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait($Password)
    Invoke-Button (Find-ById $login 'login')
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        $still = $UIA::RootElement.FindFirst($Tree::Children,
            [System.Windows.Automation.PropertyCondition]::new($UIA::AutomationIdProperty, 'EasyLangLogin'))
        if (-not $still) { break }
        Start-Sleep -Milliseconds 300
    }
    if ($still) {
        $status = Find-ById $still 'status' 2
        throw "окно входа не закрылось, статус: «$($status.Current.Name)»"
    }
    Start-Sleep -Seconds 1
    Shot 'after-login'
}

Step 'Хоткей Ctrl+Shift+E через SendInput, когда активна «сцена»' {
    $sceneWindow = $UIA::RootElement.FindFirst($Tree::Children,
        [System.Windows.Automation.PropertyCondition]::new($UIA::NameProperty, $SceneTitle))
    if (-not $sceneWindow) { throw "окно «$SceneTitle» не найдено" }
    $focused = [Desk]::Focus([IntPtr]$sceneWindow.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 500
    Note "  SetForegroundWindow: $focused, активное окно: «$([Desk]::ForegroundTitle())»"
    $sent = [Desk]::Chord(0x11, 0x10, 0x45)   # Ctrl, Shift, E
    Note "  SendInput принял событий: $sent из 6"
    $select = Find-Window 'EasyLangSelect' 15
    Note "  рамка выбора области открылась: $($select.Current.BoundingRectangle)"
    Shot 'selection-overlay'
}

Step 'Выбор области мышью поверх стоп-кадра' {
    $r = Get-Content $sceneRectFile -Raw | ConvertFrom-Json
    $x1 = [Math]::Max(0, $r.x - 4); $y1 = [Math]::Max(0, $r.y - 4)
    $x2 = $r.x + $r.w + 4; $y2 = $r.y + $r.h + 4
    Note "  тянем от ($x1, $y1) до ($x2, $y2)"
    [Desk]::Drag($x1, $y1, $x2, $y2, 25)
    $result = Find-Window 'EasyLangResult' 60
    Note "  окно результата: «$($result.Current.Name)»"
    $words = $result.FindAll($Tree::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.AutomationId -like 'word-*' } | ForEach-Object { $_.Current.Name }
    Note "  распознанные слова: $($words -join ' ')"
    if (-not $words) { throw 'в окне результата нет слов' }
    Shot 'result'
}

Step 'Клик по словам (Shift+клик — фраза) и «Разобрать»' {
    $result = Find-Window 'EasyLangResult' 5
    $links = @($result.FindAll($Tree::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.AutomationId -like 'word-*' })
    $pick = { param($text) $links | Where-Object { $_.Current.Name -ieq $text } | Select-Object -First 1 }
    $spill = & $pick 'spill'; $the = & $pick 'the'; $beans = & $pick 'beans'
    if (-not ($spill -and $the -and $beans)) { throw "не нашлись слова spill/the/beans среди: $(($links | ForEach-Object { $_.Current.Name }) -join ' ')" }
    $c = Center $spill; [Desk]::Click($c[0], $c[1]); Start-Sleep -Milliseconds 300
    [Desk]::KeyDown(0x10)
    $c = Center $the; [Desk]::Click($c[0], $c[1]); Start-Sleep -Milliseconds 300
    $c = Center $beans; [Desk]::Click($c[0], $c[1]); Start-Sleep -Milliseconds 300
    [Desk]::KeyUp(0x10)
    $phrase = Value-Of (Find-ById $result 'phrase')
    Note "  фраза в поле: «$phrase»"
    Invoke-Button (Find-ById $result 'explain')
    $output = Find-ById $result 'output'
    $deadline = (Get-Date).AddSeconds(60)
    do { Start-Sleep -Milliseconds 500; $text = Value-Of $output } while (($text -eq '' -or $text -like 'Разбираю*') -and (Get-Date) -lt $deadline)
    Note "  ответ в окне:"
    Note '```'
    Note $text
    Note '```'
    Shot 'explained'
    if ($phrase -ne 'spill the beans') { throw "ожидалась фраза «spill the beans», а в поле «$phrase»" }
    if ($text -like 'Разбираю*' -or $text -eq '') { throw 'ответ не пришёл за 60 с' }
}

foreach ($p in @($client, $scene)) { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force } }

Note ""
Note "Итого шагов с ошибкой: $failed."
$report | Set-Content -Encoding utf8 (Join-Path $OutDir 'report.md')
exit $failed
