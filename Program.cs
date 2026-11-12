using Avalonia;
using ImageMark.Models;
using ImageMark.Services;

namespace ImageMark;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Rejtett füstteszt: ImageMark.exe --selftest <bemeneti kép> <célmappa>
        if (args.Length == 3 && args[0] == "--selftest")
        {
            using var source = WatermarkRenderer.LoadOriented(args[1]);
            using var result = WatermarkRenderer.Render(source, new WatermarkPreset { Text = "SAMPLE TEXT" });
            WatermarkRenderer.Save(result, args[1], args[2], "png", 90);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
