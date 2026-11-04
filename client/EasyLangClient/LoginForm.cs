namespace EasyLangClient;

/// <summary>Вход или регистрация. Закрывается с DialogResult.OK после успешного входа.</summary>
internal sealed class LoginForm : Form
{
    private readonly ApiClient _api;
    private readonly TextBox _email = new() { Width = 260 };
    private readonly TextBox _password = new() { Width = 260, UseSystemPasswordChar = true };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(260, 0) };
    private readonly Button _login = new() { Text = "Войти", AutoSize = true };
    private readonly Button _register = new() { Text = "Регистрация", AutoSize = true };

    public LoginForm(ApiClient api, string email)
    {
        _api = api;
        _email.Text = email;
        Text = "EasyLang — вход";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        AcceptButton = _login;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = "Email", AutoSize = true });
        layout.Controls.Add(_email);
        layout.Controls.Add(new Label { Text = "Пароль (от 8 символов)", AutoSize = true });
        layout.Controls.Add(_password);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(_login);
        buttons.Controls.Add(_register);
        layout.Controls.Add(buttons);
        layout.Controls.Add(_status);
        Controls.Add(layout);

        _login.Click += async (_, _) => await SubmitAsync(register: false);
        _register.Click += async (_, _) => await SubmitAsync(register: true);
    }

    public string Email => _email.Text.Trim();

    private async Task SubmitAsync(bool register)
    {
        _login.Enabled = _register.Enabled = false;
        _status.Text = "";
        try
        {
            if (register)
                await _api.RegisterAsync(Email, _password.Text);
            else
                await _api.LoginAsync(Email, _password.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ApiException ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _login.Enabled = _register.Enabled = true;
        }
    }
}
