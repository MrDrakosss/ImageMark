using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using ImageMark.Services;

namespace ImageMark;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    private static readonly string[] SupportedLanguages = { "en", "hu", "de" };

    private static ResourceDictionary? _languageDictionary;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Settings = AppSettings.Load();
        ApplyLanguage(Settings.Language);
        ApplyTheme(Settings.Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyLanguage(string code)
    {
        if (!SupportedLanguages.Contains(code))
            code = "en";

        var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri($"avares://ImageMark/Resources/Lang.{code}.axaml"));

        var merged = Current!.Resources.MergedDictionaries;
        if (_languageDictionary != null)
            merged.Remove(_languageDictionary);

        merged.Add(dict);
        _languageDictionary = dict;
    }

    /// <summary>"light" / "dark", minden más érték a rendszer témáját követi.</summary>
    public static void ApplyTheme(string code)
    {
        Current!.RequestedThemeVariant = code switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
