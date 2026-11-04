using System.Drawing.Imaging;

namespace EasyLangClient;

internal static class ScreenCapture
{
    /// <summary>Снимок всех мониторов сразу (виртуальный экран).</summary>
    public static Bitmap CaptureVirtualScreen()
    {
        var bounds = SystemInformation.VirtualScreen;
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public static byte[] ToPng(Bitmap source, Rectangle region)
    {
        using var cropped = source.Clone(region, source.PixelFormat);
        using var stream = new MemoryStream();
        cropped.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
