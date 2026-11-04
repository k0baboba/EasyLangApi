namespace EasyLangClient;

/// <summary>
/// Выбор области: поверх всех мониторов показывается стоп-кадр экрана, затемнённый,
/// пользователь тянет прямоугольник мышью. Esc — отмена. Стоп-кадр снят до показа формы,
/// поэтому сама форма в снимок не попадает, а игра может продолжать меняться.
/// </summary>
internal sealed class RegionSelectForm : Form
{
    private readonly Bitmap _screen;
    private Point? _start;
    private Rectangle _selection;

    /// <summary>Выбранная область в координатах стоп-кадра.</summary>
    public Rectangle Selection => _selection;

    public RegionSelectForm(Bitmap screen)
    {
        _screen = screen;
        Name = "EasyLangSelect";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            _start = e.Location;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_start is { } start)
        {
            _selection = Rectangle.FromLTRB(
                Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_start is not null && e.Button == MouseButtons.Left)
        {
            _start = null;
            // Слишком маленькая область — скорее всего случайный клик
            DialogResult = _selection.Width > 8 && _selection.Height > 8 ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImage(_screen, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(120, Color.Black));
        using var region = new Region(ClientRectangle);
        region.Exclude(_selection);
        e.Graphics.FillRegion(shade, region);
        if (_selection.Width > 0 && _selection.Height > 0)
        {
            using var pen = new Pen(Color.DeepSkyBlue, 2);
            e.Graphics.DrawRectangle(pen, _selection);
        }
    }
}
