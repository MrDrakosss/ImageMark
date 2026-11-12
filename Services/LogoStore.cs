using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMark.Models;

namespace ImageMark.Services;

public static class LogoStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImageMark");

    private static readonly string FilePath = Path.Combine(Dir, "logo-presets.json");

    private static readonly string ImageDir = Path.Combine(Dir, "logos");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<LogoPreset> Load(string defaultName)
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<LogoPreset>>(File.ReadAllText(FilePath));
                if (list is { Count: > 0 })
                    return list;
            }
        }
        catch
        {
            // sérült fájl esetén alapértelmezettel indulunk
        }
        return new List<LogoPreset> { new() { Name = defaultName } };
    }

    public static void Save(IEnumerable<LogoPreset> presets)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(presets.ToList(), Options));
    }

    /// <summary>
    /// A kiválasztott logót bemásolja a program saját mappájába, így a preset akkor is
    /// működik, ha az eredeti fájlt később áthelyezik. A visszaadott érték a másolat fájlneve.
    /// </summary>
    public static string ImportImage(string sourcePath)
    {
        Directory.CreateDirectory(ImageDir);
        var bytes = File.ReadAllBytes(sourcePath);
        string name = Convert.ToHexString(SHA256.HashData(bytes))[..16]
            + Path.GetExtension(sourcePath).ToLowerInvariant();
        string target = Path.Combine(ImageDir, name);
        if (!File.Exists(target))
            File.WriteAllBytes(target, bytes);
        return name;
    }

    /// <summary>A preset logójának teljes elérési útja, vagy null, ha nincs (meg) a kép.</summary>
    public static string? ResolvePath(LogoPreset preset)
    {
        if (string.IsNullOrEmpty(preset.ImageFile))
            return null;
        string path = Path.Combine(ImageDir, Path.GetFileName(preset.ImageFile));
        return File.Exists(path) ? path : null;
    }
}
