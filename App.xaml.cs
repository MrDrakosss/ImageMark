using System.Windows;
using ImageMark.Models;
using ImageMark.Services;

namespace ImageMark;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    private static readonly string[] SupportedLanguages = { "en", "hu", "de" };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = AppSettings.Load();
        ApplyLanguage(Settings.Language);

        // Rejtett füstteszt: ImageMark.exe --selftest <bemeneti kép> <célmappa>
        if (e.Args.Length == 3 && e.Args[0] == "--selftest")
        {
            var source = WatermarkRenderer.LoadOriented(e.Args[1]);
            var result = WatermarkRenderer.Render(source, new WatermarkPreset { Text = "SAMPLE TEXT" });
            WatermarkRenderer.Save(result, e.Args[1], e.Args[2], "png", 90);
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    public static void ApplyLanguage(string code)
    {
        if (!SupportedLanguages.Contains(code))
            code = "en";

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Resources/Lang.{code}.xaml"),
        };

        var existing = Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("/Resources/Lang.") == true);
        if (existing != null)
            Current.Resources.MergedDictionaries.Remove(existing);

        Current.Resources.MergedDictionaries.Add(dict);
    }
}
