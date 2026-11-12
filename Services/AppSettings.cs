using System.IO;
using System.Text.Json;

namespace ImageMark.Services;

public class AppSettings
{
    public string Language { get; set; } = "en";

    /// <summary>"system", "light" vagy "dark".</summary>
    public string Theme { get; set; } = "system";

    public bool WatermarkEnabled { get; set; } = true;

    public bool LogoEnabled { get; set; }

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImageMark");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // sérült beállításfájl esetén alapértelmezett
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // a beállításmentés hibája nem akaszthatja meg az appot
        }
    }
}
