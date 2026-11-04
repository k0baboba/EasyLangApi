namespace EasyLangClient;

/// <summary>
/// Приложение живёт в трее. По хоткею: стоп-кадр экрана → выбор области → /recognize → окно с текстом.
/// Скриншоты одного окна (одной игры) дописываются в один эпизод на сервере,
/// чтобы предыдущие реплики попадали в контекст.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ClientSettings _settings;
    private readonly ApiClient _api;
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow? _hotkey;
    private readonly Dictionary<string, int> _episodeByTitle = new();
    private bool _busy;

    public TrayApplicationContext(ClientSettings settings, ApiClient api)
    {
        _settings = settings;
        _api = api;

        var menu = new ContextMenuStrip();
        menu.Items.Add($"Захватить область ({settings.Hotkey})", null, async (_, _) => await CaptureAsync(Native.GetForegroundWindowTitle()));
        menu.Items.Add("Войти…", null, (_, _) => ShowLogin());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "EasyLang",
            ContextMenuStrip = menu,
            Visible = true,
        };

        try
        {
            _hotkey = new HotkeyWindow(settings.Hotkey);
            // Заголовок берём в момент нажатия: потом активным станет наше окно.
            _hotkey.Pressed += async (_, _) => await CaptureAsync(Native.GetForegroundWindowTitle());
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            _tray.ShowBalloonTip(5000, "EasyLang", ex.Message, ToolTipIcon.Warning);
        }

        ShowLogin();
    }

    private void ShowLogin()
    {
        using var login = new LoginForm(_api, _settings.Email);
        if (login.ShowDialog() == DialogResult.OK)
        {
            _settings.Email = login.Email;
            _settings.TrySave();
            _tray.ShowBalloonTip(3000, "EasyLang", $"Готово. Нажмите {_settings.Hotkey}, чтобы выделить текст.", ToolTipIcon.Info);
        }
    }

    private async Task CaptureAsync(string windowTitle)
    {
        if (_busy)
            return;
        if (!_api.IsLoggedIn)
        {
            ShowLogin();
            if (!_api.IsLoggedIn)
                return;
        }

        _busy = true;
        try
        {
            using var screen = ScreenCapture.CaptureVirtualScreen();
            byte[] png;
            using (var select = new RegionSelectForm(screen))
            {
                if (select.ShowDialog() != DialogResult.OK)
                    return;
                png = ScreenCapture.ToPng(screen, select.Selection);
            }

            var title = _settings.UseWindowTitleAsTitle ? windowTitle : null;
            int? episodeId = title is not null && _episodeByTitle.TryGetValue(title, out var id) ? id : null;
            var result = await _api.RecognizeAsync(png, title, episodeId);
            if (title is not null && result.EpisodeId is { } savedEpisode)
                _episodeByTitle[title] = savedEpisode;

            new ResultForm(_api, result, title).Show();
        }
        catch (ApiException ex)
        {
            _tray.ShowBalloonTip(5000, "EasyLang", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkey?.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }
}
