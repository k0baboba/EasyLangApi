namespace EasyLangClient;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Обработчики событий в WinForms асинхронные (async void): непойманное исключение
        // придёт сюда. Показываем его, а не роняем приложение из трея молча.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "EasyLang — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);

        var settings = ClientSettings.Load();
        using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl), Timeout = TimeSpan.FromSeconds(60) };
        var api = new ApiClient(http);

        Application.Run(new TrayApplicationContext(settings, api));
    }
}
